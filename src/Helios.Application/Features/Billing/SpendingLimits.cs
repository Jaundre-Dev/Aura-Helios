using FluentValidation;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Identity;
using Helios.Contracts.Billing;
using Helios.Contracts.Organizations;
using Helios.Contracts.Requests;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Billing;

/// <summary>A monthly cap would be exceeded (402). Nothing is reserved or run.</summary>
public sealed class SpendLimitException(string message, string code) : HeliosException(message, code);

/// <summary>
/// Monthly caps on what live work may commit: per company (<c>Organization.MonthlySpendLimit</c>) and
/// per API key (<c>ApiKey.MonthlyBudget</c>), in addition to available credit. "Committed" is money
/// reserved for requests in flight plus money charged, since the start of the calendar month (UTC).
/// The check runs inside the acceptance transaction after the wallet row is locked, so concurrent
/// requests for one company are serialised and cannot jointly exceed a cap.
/// </summary>
public sealed class SpendingLimits(IHeliosDbContext db, OrganizationAccess access, IAuditWriter audit, TimeProvider clock)
{
    public const decimal MaxLimit = 10_000_000m;

    public static DateTimeOffset PeriodStart(DateTimeOffset now) => new(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

    public static bool IsValidAmount(decimal? value) =>
        value is null || (value >= 0 && value <= MaxLimit && decimal.Round(value.Value, 2) == value);

    /// <summary>Throws when <paramref name="maximumCharge"/> would take the company or key over its cap.</summary>
    public async Task EnforceAsync(Guid organizationId, Guid? apiKeyId, decimal maximumCharge, CancellationToken ct)
    {
        var start = PeriodStart(clock.GetUtcNow());

        var companyLimit = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId).Select(o => o.MonthlySpendLimit).SingleAsync(ct);

        if (companyLimit is { } limit && await CommittedAsync(organizationId, null, start, ct) + maximumCharge > limit)
        {
            throw new SpendLimitException(
                $"This request could take the company past its monthly spend limit of R{limit:0.00}.", "spend_limit_reached");
        }

        if (apiKeyId is { } keyId)
        {
            var budget = await db.ApiKeys.IgnoreQueryFilters().AsNoTracking()
                .Where(k => k.Id == keyId).Select(k => k.MonthlyBudget).SingleAsync(ct);

            if (budget is { } cap && await CommittedAsync(organizationId, keyId, start, ct) + maximumCharge > cap)
            {
                throw new SpendLimitException(
                    $"This request could take this API key past its monthly budget of R{cap:0.00}.", "key_budget_reached");
            }
        }
    }

    public async Task<SpendLimitResponse> GetAsync(Guid organizationId, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ViewBilling, ct);
        return await ResponseAsync(organizationId, ct);
    }

    public async Task<SpendLimitResponse> SetAsync(Guid organizationId, UpdateSpendLimitRequest request, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ManageBilling, ct);

        var organization = await db.Organizations.SingleAsync(o => o.Id == organizationId, ct);
        organization.MonthlySpendLimit = request.MonthlySpendLimit;

        audit.Record("billing.spend_limit.set", "Organization", organizationId.ToString(), organizationId: organizationId,
            metadataJson: $$"""{"monthlySpendLimit":{{(request.MonthlySpendLimit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")}}}""");
        await db.SaveChangesAsync(ct);

        return await ResponseAsync(organizationId, ct);
    }

    private async Task<SpendLimitResponse> ResponseAsync(Guid organizationId, CancellationToken ct)
    {
        var start = PeriodStart(clock.GetUtcNow());
        var limit = await db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.MonthlySpendLimit).SingleAsync(ct);
        return new SpendLimitResponse(limit, await CommittedAsync(organizationId, null, start, ct), start, LedgerService.Currency);
    }

    private async Task<decimal> CommittedAsync(Guid organizationId, Guid? apiKeyId, DateTimeOffset since, CancellationToken ct)
    {
        // Company-wide by design (every workspace counts), so the workspace filter is bypassed.
        var requests = db.ApiRequests.IgnoreQueryFilters()
            .Where(r => r.OrganizationId == organizationId && r.CreatedAt >= since);

        if (apiKeyId is { } keyId)
        {
            requests = requests.Where(r => r.ApiKeyId == keyId);
        }

        return await requests.SumAsync(r =>
            r.BillingState == BillingState.Reserved ? r.ReservedAmount ?? 0m
            : r.BillingState == BillingState.Settled ? r.BillingAmount
            : 0m, ct);
    }
}

public sealed class UpdateSpendLimitRequestValidator : AbstractValidator<UpdateSpendLimitRequest>
{
    public UpdateSpendLimitRequestValidator()
    {
        RuleFor(r => r.MonthlySpendLimit).Must(SpendingLimits.IsValidAmount)
            .WithMessage($"Use whole cents between 0 and R{SpendingLimits.MaxLimit:N0}, or null for no limit.");
    }
}

public sealed class UpdateKeyBudgetRequestValidator : AbstractValidator<Contracts.ApiKeys.UpdateKeyBudgetRequest>
{
    public UpdateKeyBudgetRequestValidator()
    {
        RuleFor(r => r.MonthlyBudget).Must(SpendingLimits.IsValidAmount)
            .WithMessage($"Use whole cents between 0 and R{SpendingLimits.MaxLimit:N0}, or null for no budget.");
    }
}
