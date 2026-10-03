using System.Text.Json;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Billing;
using Helios.Application.Features.Products;
using Helios.Contracts.Billing;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Platform;
using Helios.Domain.Catalogue;
using Helios.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Platform;

/// <summary>
/// Platform administration (plan section 12): tenant overview, restricted-product approvals,
/// release states, prices, credit adjustments, payment review and the audit trail. Every change
/// records who made it and why. Nothing here returns a customer's documents or results.
/// </summary>
public sealed class PlatformAdministrationService(
    IHeliosDbContext db,
    IUnitOfWork unitOfWork,
    PlatformAccess access,
    LedgerService ledger,
    PriceService prices,
    ProductExecutorRegistry executors,
    IAuditWriter audit,
    PlatformPolicy platformPolicy,
    TimeProvider clock)
{
    public const decimal MaxAdjustment = 100_000m;

    /// <summary>Prices may not take effect in the past: requests already accepted keep the price they were accepted under.</summary>
    public static readonly TimeSpan BackdatingTolerance = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyList<PlatformOrganizationResponse>> ListOrganizationsAsync(string? search, int limit, CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.ViewOperations, ct);

        var query = db.Organizations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(o => o.Name.Contains(search) || o.Slug.Contains(search));
        }

        var organizations = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(o => new
            {
                o.Id,
                o.Name,
                o.IsActive,
                o.CreatedAt,
                Members = db.OrganizationMembers.Count(m => m.OrganizationId == o.Id && m.IsActive)
            })
            .ToListAsync(ct);

        var result = new List<PlatformOrganizationResponse>(organizations.Count);
        foreach (var o in organizations)
        {
            result.Add(new PlatformOrganizationResponse(o.Id, o.Name, o.IsActive, o.CreatedAt, o.Members,
                await ledger.GetBalanceAsync(o.Id, ct)));
        }

        return result;
    }

    public async Task<IReadOnlyList<PendingEntitlementResponse>> ListPendingEntitlementsAsync(CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.ApproveEntitlements, ct);

        return await (
            from entitlement in db.Entitlements.AsNoTracking()
            where entitlement.State == EntitlementState.PendingApproval
            join organization in db.Organizations on entitlement.OrganizationId equals organization.Id
            join product in db.ApiProducts on entitlement.ProductId equals product.Id
            orderby entitlement.CreatedAt
            select new PendingEntitlementResponse(entitlement.Id, organization.Id, organization.Name, product.Slug,
                entitlement.Environment, entitlement.Purpose, entitlement.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Approves or rejects a restricted-product request. Approval still requires the product to be
    /// callable in that environment now — an approval never makes an unavailable product usable.
    /// </summary>
    public async Task<PendingEntitlementResponse> DecideEntitlementAsync(Guid entitlementId, DecideEntitlementRequest request, CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.ApproveEntitlements, ct);

        var entitlement = await db.Entitlements.SingleOrDefaultAsync(e => e.Id == entitlementId, ct)
            ?? throw new NotFoundException("Entitlement", entitlementId);

        if (entitlement.State != EntitlementState.PendingApproval)
        {
            throw new ConflictException($"This entitlement is {entitlement.State}, not awaiting approval.", "not_pending");
        }

        var product = await db.ApiProducts.AsNoTracking().SingleAsync(p => p.Id == entitlement.ProductId, ct);
        var organization = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == entitlement.OrganizationId, ct);

        if (request.Decision == ApprovalDecision.Approve &&
            !(product.IsCallableIn(entitlement.Environment) && executors.Has(product.Slug, product.CurrentVersion)))
        {
            throw new ConflictException(
                $"'{product.Slug}' is {product.ReleaseState} and cannot be used in {entitlement.Environment}.", "product_unavailable");
        }

        entitlement.State = request.Decision == ApprovalDecision.Approve ? EntitlementState.Enabled : EntitlementState.Disabled;

        audit.Record(
            request.Decision == ApprovalDecision.Approve ? "platform.entitlement.approve" : "platform.entitlement.reject",
            nameof(Entitlement), entitlement.Id.ToString(),
            organizationId: entitlement.OrganizationId,
            metadataJson: JsonSerializer.Serialize(new
            {
                product = product.Slug,
                environment = entitlement.Environment.ToString(),
                reason = request.Reason
            }));

        await db.SaveChangesAsync(ct);

        return new PendingEntitlementResponse(entitlement.Id, organization.Id, organization.Name, product.Slug,
            entitlement.Environment, entitlement.Purpose, entitlement.CreatedAt);
    }

    /// <summary>
    /// Moves a product between release states. A product can only become callable when this build
    /// has an executor and a published version for it, and only live-callable with a current live
    /// price — so the catalogue can never offer what cannot run or cannot be charged correctly.
    /// </summary>
    public async Task<ProductSummaryResponse> ChangeReleaseStateAsync(string slug, ChangeReleaseStateRequest request, CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.ManageCatalogue, ct);

        var product = await db.ApiProducts.SingleOrDefaultAsync(p => p.Slug == slug, ct)
            ?? throw new NotFoundException("Product", slug);

        var previous = product.ReleaseState;
        var callable = request.State is ProductReleaseState.Sandbox or ProductReleaseState.Beta or ProductReleaseState.Live;

        var version = product.CurrentVersion is null
            ? null
            : await db.ApiProductVersions.SingleOrDefaultAsync(v => v.ProductId == product.Id && v.Version == product.CurrentVersion, ct);

        if (callable && (version is null || !executors.Has(product.Slug, product.CurrentVersion)))
        {
            throw new ConflictException(
                $"'{slug}' has no published version with an implementation in this build; it cannot be {request.State}.",
                "no_implementation");
        }

        if (request.State is ProductReleaseState.Beta or ProductReleaseState.Live &&
            await prices.FindActiveAsync(product.Id, ApiEnvironment.Live, ct) is null)
        {
            throw new ConflictException($"Publish a live price for '{slug}' before offering it for live use.", "price_unavailable");
        }

        product.ReleaseState = request.State;
        if (version is not null)
        {
            version.ReleaseState = request.State;
        }

        audit.Record("platform.product.release_state", "ApiProduct", product.Id.ToString(),
            metadataJson: JsonSerializer.Serialize(new
            {
                product = slug,
                from = previous.ToString(),
                to = request.State.ToString(),
                reason = request.Reason
            }));

        await db.SaveChangesAsync(ct);

        return new ProductSummaryResponse(product.Slug, product.Name, product.Category, product.Summary, product.Delivery,
            product.ReleaseState, product.Sensitivity, product.BillingUnit, product.CurrentVersion,
            product.IsCallableIn(ApiEnvironment.Sandbox) && executors.Has(product.Slug, product.CurrentVersion),
            product.IsCallableIn(ApiEnvironment.Live) && executors.Has(product.Slug, product.CurrentVersion),
            product.Limitations);
    }

    public async Task<IReadOnlyList<PriceVersionResponse>> ListPricesAsync(string? productSlug, CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.PublishPrices, ct);

        var query =
            from price in db.PriceVersions.AsNoTracking()
            join product in db.ApiProducts on price.ProductId equals product.Id
            select new { price, product.Slug };

        if (!string.IsNullOrWhiteSpace(productSlug))
        {
            query = query.Where(x => x.Slug == productSlug);
        }

        var rows = await query.OrderBy(x => x.Slug).ThenByDescending(x => x.price.EffectiveFrom).ToListAsync(ct);

        return rows.Select(x => new PriceVersionResponse(x.price.Id, x.Slug, x.price.Environment, x.price.Currency,
            x.price.Unit, x.price.UnitPrice, x.price.MinimumCharge, x.price.TaxTreatment, x.price.EffectiveFrom,
            x.price.EffectiveTo)).ToList();
    }

    public async Task<PriceVersionResponse> PublishPriceAsync(PublishPriceRequest request, CancellationToken ct)
    {
        var actor = await access.RequireAsync(PlatformPermission.PublishPrices, ct);

        var now = clock.GetUtcNow();
        var effectiveFrom = request.EffectiveFrom ?? now;

        if (effectiveFrom < now - BackdatingTolerance)
        {
            throw new BadRequestException("A price cannot take effect in the past.", "price_backdated");
        }

        return await prices.PublishAsync(request.ProductSlug, request.Environment, request.Unit, request.UnitPrice,
            request.MinimumCharge, request.TaxTreatment, effectiveFrom, actor.UserId, ct);
    }

    /// <summary>
    /// An audited manual credit (positive) or debit (negative) to a company's available balance.
    /// The reference makes it idempotent: repeating it posts nothing; reusing it for a different
    /// amount is refused. A debit cannot take available credit below zero.
    /// </summary>
    public async Task<CreditAdjustmentResponse> AdjustCreditAsync(Guid organizationId, CreditAdjustmentRequest request, CancellationToken ct)
    {
        var actor = await access.RequireAsync(PlatformPermission.AdjustCredit, ct);

        if (!await db.Organizations.AnyAsync(o => o.Id == organizationId, ct))
        {
            throw new NotFoundException("Organization", organizationId);
        }

        var postingKey = $"platform:{organizationId:N}:{request.Reference}";

        var approval = await db.CreditAdjustmentApprovals.AsNoTracking()
            .SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Reference == request.Reference, ct);

        // Above the threshold, a second person must approve before anything is posted.
        if (Math.Abs(request.Amount) > platformPolicy.AdjustmentApprovalThreshold || approval is not null)
        {
            return await RequestApprovalAsync(organizationId, request, actor.UserId, approval, postingKey, ct);
        }

        var posted = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var done = await ledger.AdjustAsync(organizationId, postingKey, request.Amount, request.Reason, actor.UserId, token);

            if (!done && await PostedAmountAsync(organizationId, postingKey, token) != request.Amount)
            {
                throw new ConflictException("This reference was already used for a different adjustment.", "reference_reused");
            }

            audit.Record("platform.credit.adjust", "LedgerTransaction", postingKey,
                organizationId: organizationId,
                metadataJson: JsonSerializer.Serialize(new
                {
                    amount = request.Amount,
                    reference = request.Reference,
                    reason = request.Reason,
                    posted = done
                }));

            return done;
        }, ct);

        return new CreditAdjustmentResponse(organizationId, request.Amount, request.Reference, posted,
            await ledger.GetBalanceAsync(organizationId, ct));
    }

    private async Task<CreditAdjustmentResponse> RequestApprovalAsync(
        Guid organizationId, CreditAdjustmentRequest request, Guid requester, CreditAdjustmentApproval? existing, string postingKey, CancellationToken ct)
    {
        if (existing is not null)
        {
            // Repeating the same request is harmless; reusing its reference for something else is not.
            if (existing.Amount != request.Amount)
            {
                throw new ConflictException("This reference was already used for a different adjustment.", "reference_reused");
            }

            return new CreditAdjustmentResponse(organizationId, existing.Amount, existing.Reference,
                existing.State == AdjustmentApprovalState.Approved, await ledger.GetBalanceAsync(organizationId, ct), existing.Id);
        }

        if (await PostedAmountAsync(organizationId, postingKey, ct) is not null)
        {
            throw new ConflictException("This reference was already used for a different adjustment.", "reference_reused");
        }

        var approval = new CreditAdjustmentApproval
        {
            OrganizationId = organizationId,
            Amount = request.Amount,
            Reason = request.Reason,
            Reference = request.Reference,
            RequestedBy = requester,
            RequestedAt = clock.GetUtcNow()
        };

        db.CreditAdjustmentApprovals.Add(approval);
        audit.Record("platform.credit.approval_requested", nameof(CreditAdjustmentApproval), approval.Id.ToString(),
            organizationId: organizationId,
            metadataJson: JsonSerializer.Serialize(new { amount = request.Amount, reference = request.Reference, reason = request.Reason }));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("This reference was just used by another request.", "reference_reused");
        }

        return new CreditAdjustmentResponse(organizationId, request.Amount, request.Reference, false,
            await ledger.GetBalanceAsync(organizationId, ct), approval.Id);
    }

    public async Task<IReadOnlyList<AdjustmentApprovalResponse>> ListPendingAdjustmentsAsync(CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.AdjustCredit, ct);

        var pending = await db.CreditAdjustmentApprovals.AsNoTracking()
            .Where(a => a.State == AdjustmentApprovalState.Pending)
            .OrderBy(a => a.RequestedAt)
            .ToListAsync(ct);

        return pending.Select(ToResponse).ToList();
    }

    /// <summary>
    /// Approves (posting the adjustment) or rejects a pending one. The requester cannot decide their
    /// own request. The state change and the posting commit together; a concurrent second decision
    /// finds the request no longer pending and changes nothing.
    /// </summary>
    public async Task<AdjustmentApprovalResponse> DecideAdjustmentAsync(Guid approvalId, DecideAdjustmentRequest request, CancellationToken ct)
    {
        var actor = await access.RequireAsync(PlatformPermission.AdjustCredit, ct);

        var approval = await db.CreditAdjustmentApprovals.AsNoTracking().SingleOrDefaultAsync(a => a.Id == approvalId, ct)
            ?? throw new NotFoundException("Adjustment approval", approvalId);

        if (approval.RequestedBy == actor.UserId)
        {
            audit.Record("platform.credit.decide", nameof(CreditAdjustmentApproval), approval.Id.ToString(), allowed: false,
                denyReason: "Requester cannot decide their own adjustment.", organizationId: approval.OrganizationId);
            await db.SaveChangesAsync(ct);
            throw new ForbiddenException("A different staff member must decide this adjustment.", "four_eyes_required");
        }

        var approve = request.Decision == ApprovalDecision.Approve;
        var now = clock.GetUtcNow();

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var newState = approve ? AdjustmentApprovalState.Approved : AdjustmentApprovalState.Rejected;
            var changed = await db.CreditAdjustmentApprovals
                .Where(a => a.Id == approvalId && a.State == AdjustmentApprovalState.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.State, newState)
                    .SetProperty(a => a.DecidedBy, actor.UserId)
                    .SetProperty(a => a.DecidedAt, now)
                    .SetProperty(a => a.DecisionReason, request.Reason), token);

            if (changed != 1)
            {
                throw new ConflictException("This adjustment has already been decided.", "not_pending");
            }

            if (approve)
            {
                // Throws 402 (and rolls the decision back) if a debit would overdraw the company.
                await ledger.AdjustAsync(approval.OrganizationId, $"platform:{approval.OrganizationId:N}:{approval.Reference}",
                    approval.Amount, approval.Reason, actor.UserId, token);
            }

            audit.Record(approve ? "platform.credit.approve" : "platform.credit.reject", nameof(CreditAdjustmentApproval),
                approval.Id.ToString(), organizationId: approval.OrganizationId,
                metadataJson: JsonSerializer.Serialize(new
                {
                    amount = approval.Amount,
                    reference = approval.Reference,
                    requestedBy = approval.RequestedBy,
                    reason = request.Reason
                }));

            return true;
        }, ct);

        var decided = await db.CreditAdjustmentApprovals.AsNoTracking().SingleAsync(a => a.Id == approvalId, ct);
        return ToResponse(decided);
    }

    private static AdjustmentApprovalResponse ToResponse(CreditAdjustmentApproval a) =>
        new(a.Id, a.OrganizationId, a.Amount, a.Reason, a.Reference, a.RequestedBy, a.RequestedAt, a.State.ToString(),
            a.DecidedBy, a.DecidedAt, a.DecisionReason);

    /// <summary>
    /// Verified gateway events that changed no money and need a person: refunds and chargebacks (no
    /// reversal policy is approved yet) and authentic events that did not match their payment.
    /// </summary>
    public async Task<IReadOnlyList<PaymentEventReviewResponse>> ListPaymentEventsAsync(
        PaymentEventOutcome? outcome, int limit, CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.ViewPayments, ct);

        var query =
            from e in db.PaymentEvents.AsNoTracking()
            join p in db.Payments on e.PaymentId equals p.Id into payments
            from p in payments.DefaultIfEmpty()
            select new { e, OrganizationId = (Guid?)p.OrganizationId };

        query = outcome is { } wanted
            ? query.Where(x => x.e.Outcome == wanted)
            : query.Where(x => x.e.Outcome == PaymentEventOutcome.NeedsReview || x.e.Outcome == PaymentEventOutcome.Rejected);

        var rows = await query.OrderByDescending(x => x.e.ReceivedAt).Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct);

        return rows.Select(x => new PaymentEventReviewResponse(x.e.Id, x.e.Gateway, x.e.EventId, x.e.PaymentId,
            x.OrganizationId, x.e.Type, x.e.Amount, x.e.Currency, x.e.Outcome, x.e.Reason, x.e.ReceivedAt)).ToList();
    }

    /// <summary>The audit trail across tenants. Administrators only: rows carry addresses and actors.</summary>
    public async Task<IReadOnlyList<PlatformAuditEntry>> ListAuditAsync(
        Guid? organizationId, string? action, int limit, CancellationToken ct)
    {
        await access.RequireAsync(PlatformPermission.ViewAudit, ct);

        // Platform-wide by design; the workspace filter would hide every tenant's rows.
        var query = db.AuditLogs.IgnoreQueryFilters().AsNoTracking();

        if (organizationId is { } org)
        {
            query = query.Where(a => a.OrganizationId == org);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action.StartsWith(action));
        }

        return await query
            .OrderByDescending(a => a.OccurredAt)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(a => new PlatformAuditEntry(a.Id, a.OccurredAt, a.ActorUserId, a.OrganizationId, a.WorkspaceId,
                a.Action, a.ResourceType, a.ResourceId, a.Allowed, a.DenyReason, a.Metadata, a.CorrelationId))
            .ToListAsync(ct);
    }

    private async Task<decimal?> PostedAmountAsync(Guid organizationId, string postingKey, CancellationToken ct)
    {
        var fullKey = $"adjust:{postingKey}";

        return await (
            from transaction in db.LedgerTransactions
            where transaction.PostingKey == fullKey
            join entry in db.LedgerEntries on transaction.Id equals entry.TransactionId
            join account in db.LedgerAccounts on entry.AccountId equals account.Id
            where account.OrganizationId == organizationId && account.Type == LedgerAccountType.CustomerAvailable
            select (decimal?)entry.Amount)
            .SingleOrDefaultAsync(ct);
    }
}
