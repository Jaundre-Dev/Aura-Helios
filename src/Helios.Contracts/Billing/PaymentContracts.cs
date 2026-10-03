using System.Text.Json.Serialization;

namespace Helios.Contracts.Billing;

[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))]
public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter<PaymentEventType>))]
public enum PaymentEventType
{
    PaymentSucceeded,
    PaymentFailed,
    Refunded,
    Chargeback
}

/// <summary>What HELIOS did with a verified gateway event.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentEventOutcome>))]
public enum PaymentEventOutcome
{
    /// <summary>Changed the payment and, for a success, credited the ledger.</summary>
    Applied,

    /// <summary>Same event id seen before, or the payment was already in this state. No effect.</summary>
    Duplicate,

    /// <summary>Authentic but out of order (e.g. a failure after a success). No effect.</summary>
    Ignored,

    /// <summary>Authentic but inconsistent with the payment (amount, currency, merchant, unknown reference). No effect.</summary>
    Rejected,

    /// <summary>A refund or chargeback: recorded, no automatic ledger change until a policy is approved.</summary>
    NeedsReview
}

public sealed record CreateTopUpRequest(decimal Amount);

public sealed record PaymentResponse(
    Guid Id,
    Guid OrganizationId,
    string Gateway,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    string? CheckoutUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CreditedAt);
