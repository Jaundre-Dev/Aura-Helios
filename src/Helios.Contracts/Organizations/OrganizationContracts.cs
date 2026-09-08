namespace Helios.Contracts.Organizations;

public sealed record CreateOrganizationRequest(string Name, string? Slug = null);

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt);
