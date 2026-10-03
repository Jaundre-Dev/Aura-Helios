using System.Text.Json;
using Helios.Application.Abstractions.Execution;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Features.Billing;
using Helios.Application.Features.Products;
using Helios.Application.Features.Requests;
using Helios.Application.Features.Webhooks;
using Helios.Contracts.Requests;
using Helios.Domain.Billing;
using Helios.Domain.Execution;
using Helios.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Execution;

/// <summary>
/// Runs one claimed job to its next state. Shared by the API (synchronous products, executed inline
/// under a lease) and the worker (asynchronous products, retries, reconciliation, crash recovery),
/// so there is exactly one implementation of the money-relevant transitions.
/// <para>
/// Guarantees, in plan section 10's terms:
/// <list type="bullet">
///   <item>Fenced commits: every transition reloads the job and requires the claim's fencing token,
///   so a worker whose lease was taken over commits nothing.</item>
///   <item>Result, usage, settlement and job completion commit in one transaction; usage and the
///   settlement posting are unique per request, so no restart can charge twice.</item>
///   <item>Technical failure releases the reservation — the customer is not charged.</item>
///   <item>An ambiguous provider outcome goes to reconciliation with the reservation still held; it
///   is never blindly retried. An interrupted attempt of a non-repeatable executor is reconciled too.</item>
/// </list>
/// </para>
/// </summary>
public sealed class JobRunner(
    IHeliosDbContext db,
    IUnitOfWork unitOfWork,
    ProductExecutorRegistry executors,
    IPayloadProtector protector,
    LedgerService ledger,
    ExecutionPolicy policy,
    RequestRetentionPolicy retention,
    WebhookOutbox outbox,
    IUploadAccess uploads,
    IJobLeases leases,
    TimeProvider clock)
{
    /// <summary>A job claim that another worker superseded. Nothing was committed.</summary>
    private sealed class StaleClaimException() : Exception("The job's lease was taken over by another worker.");

    public async Task RunAsync(ClaimedJob claim, CancellationToken ct)
    {
        try
        {
            await RunClaimAsync(claim, ct);
        }
        catch (Exception ex) when (ex is StaleClaimException or DbUpdateConcurrencyException)
        {
            // Another worker owns the job now; it will finish it. Nothing of ours was committed.
        }
    }

    private async Task RunClaimAsync(ClaimedJob claim, CancellationToken ct)
    {
        var (job, request) = await LoadFencedAsync(claim, ct);
        var executor = executors.Find(job.ProductSlug, job.ProductVersion);

        if (executor is null)
        {
            await FailAsync(claim, "executor_unavailable", "No executor is registered for this product version.", ct);
            return;
        }

        var context = new ProductExecutionContext(request.Id, request.Environment, job.Attempts + 1, uploads);

        var interrupted = claim.ReclaimedFromExpiredLease && job.ExecutionStartedAt is not null;
        if (claim.Phase == JobPhase.Reconcile || (interrupted && !executor.SafeToRepeat))
        {
            await ReconcileAsync(claim, executor, context, ct);
            return;
        }

        var input = ReadInput(job, executor);

        // Commit the start of the attempt before calling out, so that if this process dies during
        // the call the next claimant knows an execution may have happened.
        job.Attempts++;
        job.ExecutionStartedAt = clock.GetUtcNow();
        job.Phase = JobPhase.Execute;
        request.Status = ApiRequestStatus.Running;
        await db.SaveChangesAsync(ct);

        ProductOutcome outcome;
        await using var heartbeat = LeaseHeartbeat.Start(leases, claim, policy, clock, ct);
        try
        {
            outcome = await executor.ExecuteAsync(input, context, heartbeat.Token);
        }
        catch (OperationCanceledException) when (heartbeat.LeaseLost && !ct.IsCancellationRequested)
        {
            // Another worker owns the job now. Commit nothing; the new owner reconciles or re-runs.
            throw new StaleClaimException();
        }
        catch (ProviderOutcomeUnknownException ex)
        {
            await MoveToReconcileAsync(claim, ex.ProviderReference, ex.Message, ct);
            return;
        }
        catch (ProductExecutionFailedException ex)
        {
            await FailAsync(claim, ex.Code, ex.Message, ct);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RetryOrFailAsync(claim, ex, ct);
            return;
        }

        await CompleteAsync(claim, outcome, ct);
    }

    private async Task ReconcileAsync(ClaimedJob claim, IProductExecutor executor, ProductExecutionContext context, CancellationToken ct)
    {
        var (job, _) = await LoadFencedAsync(claim, ct);
        job.ReconcileAttempts++;
        await db.SaveChangesAsync(ct);

        ReconcileOutcome outcome;
        await using var heartbeat = LeaseHeartbeat.Start(leases, claim, policy, clock, ct);
        try
        {
            outcome = await executor.ReconcileAsync(job.ProviderReference, context, heartbeat.Token);
        }
        catch (OperationCanceledException) when (heartbeat.LeaseLost && !ct.IsCancellationRequested)
        {
            throw new StaleClaimException();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome = ReconcileOutcome.StillUnknown;
        }

        switch (outcome.Status)
        {
            case ReconcileStatus.Completed:
                await CompleteAsync(claim, outcome.Outcome!, ct);
                break;

            case ReconcileStatus.NotCompleted:
                // Confirmed not done: safe to execute again, if attempts remain.
                await RequeueOrFailAsync(claim, "The provider confirmed the attempt did not complete.", ct);
                break;

            default:
                await StillUnknownAsync(claim, ct);
                break;
        }
    }

    private Task CompleteAsync(ClaimedJob claim, ProductOutcome outcome, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var (job, request) = await LoadFencedAsync(claim, token);
            var now = clock.GetUtcNow();

            var charge = 0m;
            if (request.PriceVersionId is { } priceVersionId)
            {
                var price = await db.PriceVersions.AsNoTracking().SingleAsync(p => p.Id == priceVersionId, token);
                charge = Math.Min(price.ChargeFor(outcome.UsageQuantity), request.ReservedAmount ?? 0m);

                await ledger.SettleAsync(request.Id, charge, token);
                request.BillingState = BillingState.Settled;
            }

            request.BillingAmount = charge;
            request.Status = outcome.ReviewRequired ? ApiRequestStatus.NeedsReview : ApiRequestStatus.Succeeded;
            request.ResultJson = outcome.Result.GetRawText();
            request.WarningsJson = outcome.Warnings.Count == 0 ? null : JsonSerializer.Serialize(outcome.Warnings);
            request.ReviewRequired = outcome.ReviewRequired;
            request.UsageUnit = outcome.UsageUnit;
            request.UsageQuantity = outcome.UsageQuantity;
            request.CompletedAt = now;
            request.ResultExpiresAt = now + retention.ResultRetention;
            request.ErrorCode = null;

            db.UsageEvents.Add(new UsageEvent
            {
                OrganizationId = request.OrganizationId,
                WorkspaceId = request.WorkspaceId,
                ApiRequestId = request.Id,
                ProductId = request.ProductId,
                ProductSlug = request.ProductSlug,
                ProductVersion = request.ProductVersion,
                Environment = request.Environment,
                PriceVersionId = request.PriceVersionId,
                Unit = outcome.UsageUnit,
                Quantity = outcome.UsageQuantity,
                Amount = charge,
                Currency = request.Currency,
                OccurredAt = now
            });

            Finish(job, JobStatus.Succeeded, now);
            await outbox.EnqueueAsync(request, token);
            return true;
        }, ct);

    private Task MoveToReconcileAsync(ClaimedJob claim, string? providerReference, string error, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var (job, request) = await LoadFencedAsync(claim, token);

            job.Status = JobStatus.Reconciling;
            job.Phase = JobPhase.Reconcile;
            job.ProviderReference = providerReference ?? job.ProviderReference;
            job.LastError = Truncate(error);
            job.AvailableAt = clock.GetUtcNow() + policy.ReconcileDelay;
            ReleaseLease(job);

            // The reservation stays held: the provider may have done (and charged for) the work.
            request.Status = ApiRequestStatus.Reconciling;
            return true;
        }, ct);

    private Task StillUnknownAsync(ClaimedJob claim, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var (job, request) = await LoadFencedAsync(claim, token);

            if (job.ReconcileAttempts >= policy.MaxReconcileAttempts)
            {
                // Out of automatic options. Money stays held until a person resolves it.
                job.Status = JobStatus.NeedsReview;
                request.Status = ApiRequestStatus.NeedsReview;
                request.ErrorCode = "outcome_unknown";
                await outbox.EnqueueAsync(request, token);
            }
            else
            {
                job.Status = JobStatus.Reconciling;
                job.AvailableAt = clock.GetUtcNow() + policy.ReconcileDelay;
                request.Status = ApiRequestStatus.Reconciling;
            }

            ReleaseLease(job);
            return true;
        }, ct);

    private Task RetryOrFailAsync(ClaimedJob claim, Exception error, CancellationToken ct) =>
        RequeueOrFailAsync(claim, error.Message, ct);

    private async Task RequeueOrFailAsync(ClaimedJob claim, string error, CancellationToken ct)
    {
        var requeued = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var (job, request) = await LoadFencedAsync(claim, token);

            if (job.Attempts >= job.MaxAttempts)
            {
                return false;
            }

            job.Status = JobStatus.Queued;
            job.Phase = JobPhase.Execute;
            job.ExecutionStartedAt = null;
            job.LastError = Truncate(error);
            job.AvailableAt = clock.GetUtcNow() + policy.RetryDelay(job.Attempts);
            ReleaseLease(job);

            request.Status = ApiRequestStatus.Queued;
            return true;
        }, ct);

        if (!requeued)
        {
            await FailAsync(claim, "processing_failed", error, ct);
        }
    }

    /// <summary>Terminal technical failure: the reservation is released in full; nothing is charged.</summary>
    private Task FailAsync(ClaimedJob claim, string errorCode, string error, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var (job, request) = await LoadFencedAsync(claim, token);
            var now = clock.GetUtcNow();

            if (request.PriceVersionId is not null)
            {
                await ledger.ReleaseAsync(request.Id, errorCode, token);
                request.BillingState = BillingState.Released;
            }

            request.Status = ApiRequestStatus.Failed;
            request.ErrorCode = errorCode;
            request.BillingAmount = 0m;
            request.CompletedAt = now;

            job.LastError = Truncate(error);
            Finish(job, JobStatus.Failed, now);
            await outbox.EnqueueAsync(request, token);
            return true;
        }, ct);

    private async Task<(Job Job, ApiRequest Request)> LoadFencedAsync(ClaimedJob claim, CancellationToken ct)
    {
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == claim.JobId, ct);

        if (job is null || job.FencingToken != claim.FencingToken || job.Status != JobStatus.Running)
        {
            throw new StaleClaimException();
        }

        var request = await db.ApiRequests.SingleAsync(r => r.Id == job.ApiRequestId, ct);
        return (job, request);
    }

    private ParsedProductInput ReadInput(Job job, IProductExecutor executor)
    {
        var body = protector.Unprotect(job.InputKeyId!, job.InputEnvelope!);
        using var document = JsonDocument.Parse(body);
        return executor.Parse(document.RootElement.Clone());
    }

    private static void Finish(Job job, JobStatus status, DateTimeOffset now)
    {
        job.Status = status;
        job.CompletedAt = now;

        // The sealed input exists only so the work can be retried or reconciled; it goes now.
        job.InputEnvelope = null;
        job.InputKeyId = null;
        ReleaseLease(job);
    }

    private static void ReleaseLease(Job job)
    {
        job.LeaseOwner = null;
        job.LeaseExpiresAt = null;
    }

    private static string Truncate(string value) => value.Length <= 1000 ? value : value[..1000];
}
