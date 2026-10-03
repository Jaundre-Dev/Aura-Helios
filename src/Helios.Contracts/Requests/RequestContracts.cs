using System.Text.Json;
using Helios.Contracts.Catalogue;

namespace Helios.Contracts.Requests;

/// <summary>Execution state, separate from billing state (plan section 10).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ApiRequestStatus>))]
public enum ApiRequestStatus
{
    Accepted,
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    NeedsReview,
    Reconciling
}

/// <summary>Where the money side of a request stands. Pending values are never shown as final.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<BillingState>))]
public enum BillingState
{
    /// <summary>Sandbox and other non-billable executions: no charge, ever.</summary>
    NotBillable,
    Pending,
    Reserved,
    Settled,
    Released
}

public sealed record UsageInfo(string Unit, decimal Quantity);

public sealed record BillingInfo(BillingState State, string Currency, decimal Amount);

/// <summary>
/// The envelope every product execution returns, synchronous or not (plan section 8). A business
/// "no" — an invalid ID number, a failed check — is a successful execution with that result,
/// not an HTTP error.
/// </summary>
public sealed record ApiRequestEnvelope(
    Guid RequestId,
    string Product,
    string Version,
    ApiEnvironment Environment,
    ApiRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    JsonElement? Result,
    IReadOnlyList<string> Warnings,
    bool ReviewRequired,
    IReadOnlyList<string> EvidenceReferences,
    UsageInfo Usage,
    BillingInfo Billing);

/// <summary>Request metadata without the result payload, for history and diagnostics.</summary>
public sealed record ApiRequestSummary(
    Guid RequestId,
    string Product,
    string Version,
    ApiEnvironment Environment,
    ApiRequestStatus Status,
    string Channel,
    Guid? ApiKeyId,
    Guid? ActorUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    UsageInfo Usage,
    BillingInfo Billing,
    bool ResultAvailable);
