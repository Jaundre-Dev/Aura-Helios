namespace Helios.Contracts.Organizations;

public sealed record CreateOrganizationRequest(string Name, string? Slug = null);

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt,
    OrganizationRole? MyRole = null);

public sealed record AddOrganizationMemberRequest(string Email, OrganizationRole Role);

public sealed record UpdateOrganizationMemberRequest(OrganizationRole Role);

public sealed record OrganizationMemberResponse(
    Guid Id,
    Guid OrganizationId,
    Guid UserId,
    string? Email,
    string? DisplayName,
    OrganizationRole Role,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>A required legal document, its current version, and whether this company accepted that version.</summary>
public sealed record AgreementStatusResponse(
    string Document,
    string CurrentVersion,
    bool Accepted,
    DateTimeOffset? AcceptedAt,
    Guid? AcceptedBy);

public sealed record AcceptAgreementRequest(string Document, string Version);
