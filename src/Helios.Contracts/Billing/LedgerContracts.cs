using System.Text.Json.Serialization;
using Helios.Contracts.Catalogue;

namespace Helios.Contracts.Billing;

/// <summary>
/// Ledger accounts. Customer accounts are liabilities (money HELIOS holds for the customer) and
/// carry credit-positive balances; gateway clearing is an asset and runs negative. Across all
/// accounts the balances always sum to zero.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LedgerAccountType>))]
public enum LedgerAccountType
{
    /// <summary>Prepaid funds the customer can spend now.</summary>
    CustomerAvailable,

    /// <summary>Funds held for requests in flight; neither spendable nor earned.</summary>
    CustomerReserved,

    /// <summary>Platform revenue from settled usage.</summary>
    Revenue,

    /// <summary>Money received from the payment gateway, awaiting bank settlement.</summary>
    GatewayClearing,

    /// <summary>Platform-side counterpart for audited manual credit adjustments.</summary>
    Adjustments
}

[JsonConverter(typeof(JsonStringEnumConverter<LedgerTransactionType>))]
public enum LedgerTransactionType
{
    TopUp,
    Reserve,
    Settle,
    Release,
    Adjustment,
    Reversal
}

[JsonConverter(typeof(JsonStringEnumConverter<ReservationState>))]
public enum ReservationState
{
    Held,
    Settled,
    Released
}

/// <summary>
/// Settled is all money held for the customer; reserved is the part committed to requests in
/// flight; available is what new requests can reserve. Settled = available + reserved.
/// </summary>
public sealed record BalanceResponse(
    string Currency,
    decimal Settled,
    decimal Reserved,
    decimal Available);

public sealed record LedgerTransactionResponse(
    Guid Id,
    LedgerTransactionType Type,
    string Description,
    decimal AvailableChange,
    decimal ReservedChange,
    string Currency,
    Guid? ApiRequestId,
    Guid? PaymentId,
    DateTimeOffset CreatedAt);

public sealed record PriceVersionResponse(
    Guid Id,
    string ProductSlug,
    ApiEnvironment Environment,
    string Currency,
    string Unit,
    decimal UnitPrice,
    decimal MinimumCharge,
    string TaxTreatment,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record UsageLine(
    string Product,
    ApiEnvironment Environment,
    string Unit,
    decimal Quantity,
    decimal Amount,
    int Requests);

public sealed record UsageResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    IReadOnlyList<UsageLine> Lines,
    decimal Total);
