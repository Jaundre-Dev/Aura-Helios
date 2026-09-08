using Helios.Contracts.Common;

namespace Helios.Contracts.Projects;

public sealed record CreateProjectRequest(
    string Name,
    string? Slug = null,
    string? Description = null,
    DataClassification Classification = DataClassification.Internal);

public sealed record UpdateProjectRequest(
    string? Name = null,
    string? Description = null,
    DataClassification? Classification = null,
    bool? IsActive = null);

public sealed record ProjectResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Slug,
    string? Description,
    DataClassification Classification,
    bool IsActive,
    DateTimeOffset CreatedAt);
