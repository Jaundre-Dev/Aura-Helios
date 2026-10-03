using System.Text.Json;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Abstractions.Execution;
using Helios.Application.Features.Billing;
using Helios.Application.Features.Catalogue;
using Helios.Application.Features.Execution;
using Helios.Application.Features.Webhooks;
using Helios.Application.Features.Identity;
using Helios.Application.Features.Products;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.Contracts.Requests;
using Helios.Domain.ApiKeys;
using Helios.Domain.Execution;
using Helios.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Requests;

/// <summary>Retention for stored result payloads; request metadata is kept independently.</summary>
public sealed record RequestRetentionPolicy(TimeSpan ResultRetention);

/// <summary>
/// The outcome of an execution call: the envelope, whether it replayed an earlier request, and
/// whether it was only accepted (still queued, running or reconciling) rather than finished.
/// </summary>
public sealed record ExecutionResult(ApiRequestEnvelope Envelope, bool Replayed, bool Accepted = false);

/// <summary>
/// Executes products and serves request records. One path for API keys and signed-in users, so
/// manual portal runs get the same authorisation, bounds and records as API calls (plan section 4).
/// <para>
/// Plan section 6 request flow: caller and environment → product exists and is callable →
/// entitlement → key scope → bounds and input validation → price → idempotency → one transaction
/// creating the request, its durable job and (live) the credit reservation → execute through the
/// shared <see cref="JobRunner"/>. Nothing is recorded or reserved for a request rejected before
/// acceptance, and nothing in sandbox is ever billable.
/// </para>
/// </summary>
public sealed class ProductRequestService(
    IHeliosDbContext db,
    IUnitOfWork unitOfWork,
    IWorkspaceContext context,
    OrganizationAccess access,
    CatalogueService catalogue,
    ProductExecutorRegistry executors,
    IRequestFingerprinter fingerprinter,
    IPayloadProtector protector,
    PriceService prices,
    LedgerService ledger,
    JobRunner runner,
    ExecutionPolicy policy,
    WebhookOutbox outbox,
    TimeProvider clock)
{
    public const int MaxIdempotencyKeyLength = 255;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Caller(
        Guid OrganizationId,
        Guid WorkspaceId,
        ApiEnvironment Environment,
        ApiKey? Key,
        Guid? UserId)
    {
        public string Channel => Key is null ? "portal" : "api";
    }

    public async Task<ExecutionResult> ExecuteAsync(
        string productSlug,
        ApiEnvironment? requestedEnvironment,
        string? idempotencyKey,
        JsonElement body,
        int bodyBytes,
        CancellationToken ct)
    {
        var caller = await ResolveCallerAsync(requestedEnvironment, [OrganizationPermission.ExecuteProducts], ct);

        var product = await db.ApiProducts.AsNoTracking().SingleOrDefaultAsync(p => p.Slug == productSlug, ct)
            ?? throw new NotFoundException("Product", productSlug);

        if (!catalogue.IsCallable(product, caller.Environment))
        {
            throw new ConflictException(
                $"'{product.Slug}' is {product.ReleaseState} and cannot be called in {caller.Environment}.",
                "product_unavailable");
        }

        var entitled = await db.Entitlements.AnyAsync(e =>
            e.OrganizationId == caller.OrganizationId &&
            e.ProductId == product.Id &&
            e.Environment == caller.Environment &&
            e.State == EntitlementState.Enabled, ct);

        if (!entitled)
        {
            throw new ForbiddenException(
                $"'{product.Slug}' is not enabled for {caller.Environment} on this company.", "product_not_enabled");
        }

        if (caller.Key is { } key && !key.ScopeList.Contains(product.Slug, StringComparer.Ordinal))
        {
            throw new ForbiddenException($"This API key is not scoped for '{product.Slug}'.", "key_scope");
        }

        var executor = executors.Find(product.Slug, product.CurrentVersion)!;

        var maxInputBytes = await db.ApiProductVersions
            .Where(v => v.ProductId == product.Id && v.Version == executor.Version)
            .Select(v => (int?)v.MaxInputBytes)
            .SingleOrDefaultAsync(ct)
            ?? throw new ConflictException($"'{product.Slug}' has no published version {executor.Version}.", "product_unavailable");

        if (bodyBytes > maxInputBytes)
        {
            throw new PayloadTooLargeException(maxInputBytes);
        }

        var input = executor.Parse(body);

        // Live work costs money: it must be safely retryable, and it needs a published price.
        var billable = caller.Environment == ApiEnvironment.Live;
        if (billable && idempotencyKey is null)
        {
            throw new BadRequestException(
                "Billable requests require an Idempotency-Key header so a retry can never charge twice.",
                "idempotency_key_required");
        }

        var price = billable
            ? await prices.FindActiveAsync(product.Id, caller.Environment, ct)
              ?? throw new ConflictException($"'{product.Slug}' has no current {caller.Environment} price.", "price_unavailable")
            : null;

        var maximumCharge = price?.ChargeFor(executor.EstimateMaxUnits(input));
        var fingerprint = fingerprinter.Compute($"{product.Slug}/{executor.Version}\n{input.Canonical}");

        if (idempotencyKey is not null &&
            await FindReplayAsync(caller, product.Slug, executor.Version, idempotencyKey, input, ct) is { } replay)
        {
            return replay;
        }

        var sealedInput = protector.Protect(body.GetRawText());
        var inline = executor.Mode == ExecutionMode.Synchronous;

        Guid requestId, jobId;
        long fencingToken;
        try
        {
            (requestId, jobId, fencingToken) = await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var now = clock.GetUtcNow();

                var request = new ApiRequest
                {
                    OrganizationId = caller.OrganizationId,
                    WorkspaceId = caller.WorkspaceId,
                    ProductId = product.Id,
                    ProductSlug = product.Slug,
                    ProductVersion = executor.Version,
                    Environment = caller.Environment,
                    Channel = caller.Channel,
                    ActorUserId = caller.UserId,
                    ApiKeyId = caller.Key?.Id,
                    Status = inline ? ApiRequestStatus.Running : ApiRequestStatus.Queued,
                    IdempotencyKey = idempotencyKey,
                    FingerprintKeyId = fingerprint.KeyId,
                    PayloadFingerprint = fingerprint.Value,
                    UsageUnit = price?.Unit ?? "request",
                    UsageQuantity = 0m,
                    BillingState = billable ? BillingState.Reserved : BillingState.NotBillable,
                    Currency = LedgerService.Currency,
                    BillingAmount = 0m,
                    PriceVersionId = price?.Id,
                    ReservedAmount = maximumCharge,
                    CreatedAt = now
                };

                // A synchronous product is claimed by this API instance at creation, under a normal
                // lease: if this process dies mid-request, a worker reclaims and finishes it.
                var job = new Job
                {
                    ApiRequestId = request.Id,
                    OrganizationId = caller.OrganizationId,
                    WorkspaceId = caller.WorkspaceId,
                    ProductSlug = product.Slug,
                    ProductVersion = executor.Version,
                    Status = inline ? JobStatus.Running : JobStatus.Queued,
                    AvailableAt = now,
                    LeaseOwner = inline ? InlineWorkerId : null,
                    LeaseExpiresAt = inline ? now + policy.Lease : null,
                    FencingToken = inline ? 1 : 0,
                    InputEnvelope = sealedInput.Envelope,
                    InputKeyId = sealedInput.KeyId,
                    CreatedAt = now
                };

                db.ApiRequests.Add(request);
                db.Jobs.Add(job);

                // Throws 402 — and rolls the whole acceptance back — when credit does not cover
                // the maximum. The wallet row stays locked until commit, so concurrent requests
                // queue behind each other and can never jointly overspend.
                if (billable)
                {
                    await ledger.ReserveAsync(caller.OrganizationId, request.Id, maximumCharge!.Value, token);
                }

                return (request.Id, job.Id, job.FencingToken);
            }, ct);
        }
        catch (DbUpdateException) when (idempotencyKey is not null)
        {
            // A concurrent request with the same idempotency key committed first. Answer as a
            // replay of it (or a conflict if the input differs) instead of a duplicate.
            var winner = await FindReplayAsync(caller, product.Slug, executor.Version, idempotencyKey, input, ct);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }

        if (inline)
        {
            await runner.RunAsync(
                new ClaimedJob(jobId, caller.OrganizationId, caller.WorkspaceId, fencingToken, JobPhase.Execute, false), ct);
        }

        var stored = await db.ApiRequests.AsNoTracking().SingleAsync(r => r.Id == requestId, ct);
        return new ExecutionResult(ToEnvelope(stored, includeResult: true), Replayed: false, Accepted: !IsTerminal(stored.Status));
    }

    /// <summary>
    /// Best-effort cancellation. A request still waiting in the queue is cancelled and its
    /// reservation released in full. One already running, reconciling or finished is not: work may
    /// have happened at a provider, and completed billable steps follow the product's policy.
    /// </summary>
    public async Task<ApiRequestSummary?> CancelAsync(Guid id, CancellationToken ct)
    {
        if (await FindReadableAsync(id, resultAccess: false, ct, OrganizationPermission.ExecuteProducts) is null)
        {
            return null;
        }

        try
        {
            await CancelQueuedAsync(id, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A worker claimed the job between our read and our write (its claim moved the fencing
            // token, so our conditional update matched nothing). It is running now.
            throw new ConflictException("This request has started and can no longer be cancelled.", "not_cancellable");
        }

        return await GetAsync(id, ct);
    }

    private Task CancelQueuedAsync(Guid id, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var job = await db.Jobs.SingleAsync(j => j.ApiRequestId == id, token);
            var request = await db.ApiRequests.SingleAsync(r => r.Id == id, token);

            if (job.Status != JobStatus.Queued)
            {
                throw new ConflictException(
                    $"This request is {request.Status} and can no longer be cancelled.", "not_cancellable");
            }

            if (request.PriceVersionId is not null)
            {
                await ledger.ReleaseAsync(id, "cancelled", token);
                request.BillingState = BillingState.Released;
            }

            var now = clock.GetUtcNow();
            request.Status = ApiRequestStatus.Cancelled;
            request.CompletedAt = now;
            request.ErrorCode = "cancelled";

            // Saved conditionally on the fencing token read above: if a worker claimed the job in
            // the meantime this update matches nothing and the whole cancellation rolls back.
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = now;
            job.InputEnvelope = null;
            job.InputKeyId = null;
            await outbox.EnqueueAsync(request, token);
            return true;
        }, ct);

    private static string InlineWorkerId => $"api:{System.Environment.MachineName}";

    private static bool IsTerminal(ApiRequestStatus status) =>
        status is ApiRequestStatus.Succeeded or ApiRequestStatus.Failed or ApiRequestStatus.Cancelled
            or ApiRequestStatus.NeedsReview;

    /// <summary>Request metadata. Never includes the result payload.</summary>
    public async Task<ApiRequestSummary?> GetAsync(Guid id, CancellationToken ct)
    {
        var request = await FindReadableAsync(id, resultAccess: false, ct);
        return request is null ? null : ToSummary(request);
    }

    /// <summary>
    /// The full envelope including the result. Signed-in users need <c>ViewResults</c> — Finance and
    /// Developer roles do not have it. A key reads results only for products it is scoped to.
    /// </summary>
    public async Task<ApiRequestEnvelope?> GetResultAsync(Guid id, CancellationToken ct)
    {
        var request = await FindReadableAsync(id, resultAccess: true, ct);
        if (request is null)
        {
            return null;
        }

        if (request.ResultJson is null || request.ResultExpiresAt <= clock.GetUtcNow())
        {
            throw new GoneException("This request's result is past its retention period and is no longer available.");
        }

        return ToEnvelope(request, includeResult: true);
    }

    public async Task<IReadOnlyList<ApiRequestSummary>> ListAsync(
        ApiEnvironment? environment,
        string? productSlug,
        int limit,
        CancellationToken ct)
    {
        var caller = await ResolveCallerAsync(environment, ReadPermissions(resultAccess: false), ct);

        var query = db.ApiRequests.AsNoTracking().Where(r => r.WorkspaceId == caller.WorkspaceId);

        if (caller.Key is not null || environment is not null)
        {
            query = query.Where(r => r.Environment == caller.Environment);
        }

        if (caller.Key is { } key)
        {
            var scopes = key.ScopeList.ToList();
            query = query.Where(r => scopes.Contains(r.ProductSlug));
        }

        if (!string.IsNullOrWhiteSpace(productSlug))
        {
            query = query.Where(r => r.ProductSlug == productSlug);
        }

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        return requests.Select(ToSummary).ToList();
    }

    private async Task<ApiRequest?> FindReadableAsync(
        Guid id, bool resultAccess, CancellationToken ct, OrganizationPermission? requiredInstead = null)
    {
        var caller = await ResolveCallerAsync(null,
            requiredInstead is { } permission ? [permission] : ReadPermissions(resultAccess), ct);

        // The workspace query filter already confines this to the caller's workspace; a request
        // from another workspace or company is simply not found.
        var request = await db.ApiRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);

        if (request is null || request.WorkspaceId != caller.WorkspaceId)
        {
            return null;
        }

        if (caller.Key is { } key &&
            (request.Environment != key.Environment || !key.ScopeList.Contains(request.ProductSlug, StringComparer.Ordinal)))
        {
            return null;
        }

        return request;
    }

    private static OrganizationPermission[] ReadPermissions(bool resultAccess) =>
        resultAccess
            ? [OrganizationPermission.ViewResults]
            : [OrganizationPermission.ViewResults, OrganizationPermission.ViewRequestDiagnostics];

    private async Task<ExecutionResult?> FindReplayAsync(
        Caller caller,
        string productSlug,
        string version,
        string idempotencyKey,
        ParsedProductInput input,
        CancellationToken ct)
    {
        // Idempotency identity spans the company and environment, not the workspace.
        var existing = await db.ApiRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(r =>
                r.OrganizationId == caller.OrganizationId &&
                r.Environment == caller.Environment &&
                r.ProductSlug == productSlug &&
                r.ProductVersion == version &&
                r.IdempotencyKey == idempotencyKey, ct);

        if (existing is null)
        {
            return null;
        }

        var fingerprint = fingerprinter.Compute(existing.FingerprintKeyId, $"{productSlug}/{version}\n{input.Canonical}");
        if (!string.Equals(fingerprint, existing.PayloadFingerprint, StringComparison.Ordinal) ||
            existing.WorkspaceId != caller.WorkspaceId)
        {
            throw new ConflictException(
                "This Idempotency-Key was already used with a different request.", "idempotency_key_reused");
        }

        return new ExecutionResult(
            ToEnvelope(existing, includeResult: existing.ResultExpiresAt > clock.GetUtcNow()),
            Replayed: true,
            Accepted: !IsTerminal(existing.Status));
    }

    private async Task<Caller> ResolveCallerAsync(
        ApiEnvironment? requestedEnvironment,
        IReadOnlyCollection<OrganizationPermission> userPermissions,
        CancellationToken ct)
    {
        if (context.ApiKeyId is { } keyId)
        {
            // Authentication already verified this key is live; read it for its authoritative scope.
            var key = await db.ApiKeys.IgnoreQueryFilters().AsNoTracking().SingleAsync(k => k.Id == keyId, ct);

            if (requestedEnvironment is { } env && env != key.Environment)
            {
                throw new ForbiddenException(
                    $"This is a {key.Environment} key and cannot act in {env}.", "key_environment_mismatch");
            }

            return new Caller(key.OrganizationId, key.WorkspaceId, key.Environment, key, null);
        }

        var userId = context.UserId ?? throw new UnauthenticatedException();
        var workspaceId = context.WorkspaceId
            ?? throw new ForbiddenException("Select a workspace first.", "workspace_required");

        var organizationId = await db.Workspaces
            .Where(w => w.Id == workspaceId)
            .Select(w => w.OrganizationId)
            .SingleAsync(ct);

        await access.RequireAnyAsync(organizationId, userPermissions, ct);

        return new Caller(organizationId, workspaceId, requestedEnvironment ?? ApiEnvironment.Sandbox, null, userId);
    }

    private static ApiRequestEnvelope ToEnvelope(ApiRequest r, bool includeResult) =>
        new(r.Id,
            r.ProductSlug,
            r.ProductVersion,
            r.Environment,
            r.Status,
            r.CreatedAt,
            r.CompletedAt,
            includeResult && r.ResultJson is not null ? JsonDocument.Parse(r.ResultJson).RootElement.Clone() : null,
            r.WarningsJson is null ? [] : JsonSerializer.Deserialize<List<string>>(r.WarningsJson, Json) ?? [],
            r.ReviewRequired,
            [],
            new UsageInfo(r.UsageUnit, r.UsageQuantity),
            Billing(r),
            r.ErrorCode);

    private ApiRequestSummary ToSummary(ApiRequest r) =>
        new(r.Id, r.ProductSlug, r.ProductVersion, r.Environment, r.Status, r.Channel, r.ApiKeyId, r.ActorUserId,
            r.CreatedAt, r.CompletedAt,
            new UsageInfo(r.UsageUnit, r.UsageQuantity),
            Billing(r),
            ResultAvailable: r.ResultJson is not null && r.ResultExpiresAt > clock.GetUtcNow(),
            Error: r.ErrorCode);

    /// <summary>Amount is what was charged; while money is only held, the held maximum is shown separately.</summary>
    private static BillingInfo Billing(ApiRequest r) =>
        new(r.BillingState, r.Currency, r.BillingAmount,
            r.BillingState == BillingState.Reserved ? r.ReservedAmount : null);
}
