using System.Text.Json.Serialization;
using Helios.Contracts.Billing;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Requests;

namespace Helios.Contracts.Platform;

/// <summary>
/// A HELIOS staff role. Separate from customer company roles: being platform staff grants nothing
/// inside a customer company, and no platform role can read result payloads or documents.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlatformRole>))]
public enum PlatformRole
{
    /// <summary>Staff, catalogue, approvals, prices, credit and operations.</summary>
    Administrator,

    /// <summary>Prices, credit adjustments, payment review and stuck-request resolution.</summary>
    Finance,

    /// <summary>Operational queues and webhook redelivery; no money, no staff, no approvals.</summary>
    Support
}

[JsonConverter(typeof(JsonStringEnumConverter<PlatformPermission>))]
public enum PlatformPermission
{
    ManageStaff,
    ManageCatalogue,
    ApproveEntitlements,
    PublishPrices,
    AdjustCredit,
    ResolveRequests,
    ViewOperations,
    RetryDeliveries,
    ViewPayments,
    ViewAudit
}

// Staff and step-up sign-in

public sealed record GrantPlatformRoleRequest(string Email, PlatformRole Role);

public sealed record PlatformStaffResponse(
    Guid UserId,
    string? Email,
    string? DisplayName,
    PlatformRole Role,
    bool IsActive,
    bool MfaConfirmed,
    DateTimeOffset GrantedAt,
    Guid? GrantedBy);

/// <summary>Shown once, at enrolment. The secret cannot be read back afterwards.</summary>
public sealed record MfaEnrolmentResponse(string Secret, string OtpAuthUri);

public sealed record MfaCodeRequest(string Code);

/// <summary>
/// A short-lived, platform-scoped token issued only after a valid authenticator code. It is the
/// only credential the platform endpoints accept.
/// </summary>
public sealed record PlatformSessionResponse(string AccessToken, DateTimeOffset ExpiresAt, PlatformRole Role);

// Tenants, approvals, catalogue and money

public sealed record PlatformOrganizationResponse(
    Guid Id,
    string Name,
    bool IsActive,
    DateTimeOffset CreatedAt,
    int ActiveMembers,
    BalanceResponse Balance);

public sealed record PendingEntitlementResponse(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string ProductSlug,
    ApiEnvironment Environment,
    string? Purpose,
    DateTimeOffset RequestedAt);

[JsonConverter(typeof(JsonStringEnumConverter<ApprovalDecision>))]
public enum ApprovalDecision
{
    Approve,
    Reject
}

public sealed record DecideEntitlementRequest(ApprovalDecision Decision, string Reason);

public sealed record PublishPriceRequest(
    string ProductSlug,
    ApiEnvironment Environment,
    string Unit,
    decimal UnitPrice,
    decimal MinimumCharge,
    string TaxTreatment,
    DateTimeOffset? EffectiveFrom);

/// <param name="Reference">Caller-chosen and unique: repeating a reference never posts twice.</param>
public sealed record CreditAdjustmentRequest(decimal Amount, string Reason, string Reference);

public sealed record CreditAdjustmentResponse(Guid OrganizationId, decimal Amount, string Reference, bool Posted, BalanceResponse Balance);

public sealed record ChangeReleaseStateRequest(ProductReleaseState State, string Reason);

public sealed record PaymentEventReviewResponse(
    Guid Id,
    string Gateway,
    string EventId,
    Guid? PaymentId,
    Guid? OrganizationId,
    PaymentEventType Type,
    decimal Amount,
    string Currency,
    PaymentEventOutcome Outcome,
    string? Reason,
    DateTimeOffset ReceivedAt);

// Operations

/// <summary>Job metadata for operations. Never includes input or result payloads.</summary>
public sealed record PlatformJobResponse(
    Guid JobId,
    Guid RequestId,
    Guid OrganizationId,
    string Product,
    string Version,
    ApiEnvironment Environment,
    string Status,
    ApiRequestStatus RequestStatus,
    BillingState BillingState,
    decimal? ReservedAmount,
    int Attempts,
    int ReconcileAttempts,
    string? ProviderReference,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset AvailableAt);

[JsonConverter(typeof(JsonStringEnumConverter<ResolutionAction>))]
public enum ResolutionAction
{
    /// <summary>Give up on the unknown outcome: release the held money and fail the request.</summary>
    Release,

    /// <summary>Ask the provider again from the start of the reconciliation budget.</summary>
    Reconcile
}

public sealed record ResolveRequestRequest(ResolutionAction Action, string Reason);

public sealed record PlatformDeliveryResponse(
    Guid Id,
    Guid OrganizationId,
    Guid EndpointId,
    string EventId,
    string EventType,
    string Status,
    int Attempts,
    int? LastStatusCode,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextAttemptAt);

public sealed record PlatformAuditEntry(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    Guid? OrganizationId,
    Guid? WorkspaceId,
    string Action,
    string ResourceType,
    string? ResourceId,
    bool Allowed,
    string? DenyReason,
    string? Metadata,
    string? CorrelationId);
