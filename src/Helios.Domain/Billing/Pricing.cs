using Helios.Contracts.Catalogue;
using Helios.Domain.Common;

namespace Helios.Domain.Billing;

/// <summary>
/// The Rand price of one product unit in one environment for a period. Immutable once created:
/// a change is a new version with a later <see cref="EffectiveFrom"/>, and every request records
/// the version it was charged under. Prices are set by the platform, never seeded as approved.
/// </summary>
public class PriceVersion : Entity
{
    public Guid ProductId { get; set; }
    public ApiEnvironment Environment { get; set; }
    public required string Currency { get; set; }
    public required string Unit { get; set; }

    /// <summary>Rand per unit, six decimal places so sub-cent unit prices are exact.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Smallest charge for one successful request.</summary>
    public decimal MinimumCharge { get; set; }

    /// <summary>How VAT applies, e.g. <c>vat_exclusive_standard</c>; invoicing reads it.</summary>
    public required string TaxTreatment { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public const int Precision = 6;

    /// <summary>
    /// The charge for a quantity: unit price × quantity, never below the minimum, rounded half away
    /// from zero to six places. Invoice totals round to cents separately.
    /// </summary>
    public decimal ChargeFor(decimal quantity) =>
        Math.Max(MinimumCharge, Math.Round(UnitPrice * quantity, Precision, MidpointRounding.AwayFromZero));
}

/// <summary>
/// The immutable record that a request consumed metered units and what it cost. Unique per
/// request, so restarts and retries can never settle the same usage twice.
/// </summary>
public class UsageEvent : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApiRequestId { get; set; }
    public Guid ProductId { get; set; }
    public required string ProductSlug { get; set; }
    public required string ProductVersion { get; set; }
    public ApiEnvironment Environment { get; set; }
    public Guid? PriceVersionId { get; set; }
    public required string Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
