using Helios.Contracts.Identity;

namespace Helios.Contracts.Workspaces;

public sealed record CreateWorkspaceRequest(
    Guid OrganizationId,
    string Name,
    string? Slug = null,
    string? Description = null);

public sealed record UpdateWorkspaceRequest(
    string? Name = null,
    string? Description = null,
    bool? IsActive = null);

public sealed record WorkspaceResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Slug,
    string? Description,
    bool IsActive,
    WorkspaceRole? MyRole,
    DateTimeOffset CreatedAt);

public sealed record AddWorkspaceMemberRequest(Guid UserId, WorkspaceRole Role);

public sealed record WorkspaceMemberResponse(
    Guid Id,
    Guid WorkspaceId,
    Guid UserId,
    WorkspaceRole Role,
    DateTimeOffset CreatedAt);
