using Helios.Contracts.Billing;
using Helios.Domain.Common;

namespace Helios.Domain.Billing;

/// <summary>
/// A top-up a company started with the payment gateway. Credit is posted only when a verified
/// gateway event confirms it — never from the browser's return redirect — and at most once.
/// </summary>
public class Payment : Entity, IAggregateRoot
{
    public Guid OrganizationId { get; set; }
    public required string Gateway { get; set; }

    /// <summary>The gateway's id for this checkout; callbacks are matched on it.</summary>
    public string? GatewayReference { get; set; }

    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? CheckoutUrl { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CreditedAt { get; set; }
}

/// <summary>
/// Every authenticated gateway event received, whether applied or not, keyed uniquely by gateway
/// and event id so a replayed or duplicated callback is recognised and has no second effect.
/// </summary>
public class PaymentEvent : Entity
{
    public required string Gateway { get; set; }
    public required string EventId { get; set; }
    public Guid? PaymentId { get; set; }
    public PaymentEventType Type { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public PaymentEventOutcome Outcome { get; set; }
    public string? Reason { get; set; }

    /// <summary>SHA-256 of the raw callback body, for dispute evidence without storing card data.</summary>
    public required string PayloadHash { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}
