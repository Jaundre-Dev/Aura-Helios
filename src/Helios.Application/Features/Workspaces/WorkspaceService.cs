using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Contracts.Identity;
using Helios.Contracts.Workspaces;
using Helios.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Workspaces;

public sealed class WorkspaceService(
    IHeliosDbContext db,
    IWorkspaceContext context,
    IAuditWriter audit)
{
    /// <summary>
    /// Every workspace the caller belongs to.
    /// </summary>
    /// <remarks>
    /// Uses <c>IgnoreQueryFilters</c> deliberately. The global filter pins reads to the
    /// caller's <em>currently selected</em> workspace, which is right for every other
    /// query and wrong for this one — you cannot switch to a workspace you cannot list.
    /// The membership join is the real authorization here, and it is narrower than the
    /// filter it replaces: a row exists only where access was explicitly granted.
    /// </remarks>
    public async Task<IReadOnlyList<WorkspaceResponse>> ListMineAsync(CancellationToken ct)
    {
        var userId = RequireUser();

        // Order on the entity, before projecting. Ordering after the Select cannot be
        // translated: EF has no way to map a constructed record's property back to a column.
        var query =
            from member in db.WorkspaceMembers.IgnoreQueryFilters()
            where member.UserId == userId
            join workspace in db.Workspaces.IgnoreQueryFilters()
                on member.WorkspaceId equals workspace.Id
            orderby workspace.Name
            select new WorkspaceResponse(
                workspace.Id,
                workspace.OrganizationId,
                workspace.Name,
                workspace.Slug,
                workspace.Description,
                workspace.IsActive,
                member.Role,
                workspace.CreatedAt);

        return await query.ToListAsync(ct);
    }

    public async Task<WorkspaceResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var userId = RequireUser();

        var membership = await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(m => m.WorkspaceId == id && m.UserId == userId, ct);

        if (membership is null)
        {
            // Deliberately indistinguishable from "does not exist": telling a stranger
            // that a workspace is real leaks the existence of other tenants.
            return null;
        }

        var workspace = await db.Workspaces
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(w => w.Id == id, ct);

        return workspace is null ? null : ToResponse(workspace, membership.Role);
    }

    public async Task<WorkspaceResponse> CreateAsync(CreateWorkspaceRequest request, CancellationToken ct)
    {
        var userId = RequireUser();

        var organisationExists = await db.Organizations.AnyAsync(o => o.Id == request.OrganizationId, ct);
        if (!organisationExists)
        {
            throw new NotFoundException("Organization", request.OrganizationId);
        }

        var slug = Slug.From(request.Slug ?? request.Name);

        var slugTaken = await db.Workspaces
            .IgnoreQueryFilters()
            .AnyAsync(w => w.OrganizationId == request.OrganizationId && w.Slug == slug, ct);

        if (slugTaken)
        {
            throw new ConflictException($"A workspace with the slug '{slug}' already exists in this organization.");
        }

        var workspace = new Workspace
        {
            OrganizationId = request.OrganizationId,
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description?.Trim()
        };

        // The creator becomes Owner in the same transaction. A workspace nobody can
        // reach is not a useful failure mode.
        var membership = new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = WorkspaceRole.Owner
        };

        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(membership);

        audit.Record("workspace.create", nameof(Workspace), workspace.Id.ToString());

        await db.SaveChangesAsync(ct);

        return ToResponse(workspace, WorkspaceRole.Owner);
    }

    public async Task<WorkspaceResponse> UpdateAsync(Guid id, UpdateWorkspaceRequest request, CancellationToken ct)
    {
        var role = await RequireRoleAsync(id, WorkspaceRole.Admin, ct);

        var workspace = await db.Workspaces
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException("Workspace", id);

        if (request.Name is { } name)
        {
            workspace.Name = name.Trim();
        }

        if (request.Description is { } description)
        {
            workspace.Description = description.Trim();
        }

        if (request.IsActive is { } isActive)
        {
            workspace.IsActive = isActive;
        }

        audit.Record("workspace.update", nameof(Workspace), id.ToString());

        await db.SaveChangesAsync(ct);

        return ToResponse(workspace, role);
    }

    public async Task<IReadOnlyList<WorkspaceMemberResponse>> ListMembersAsync(Guid workspaceId, CancellationToken ct)
    {
        await RequireRoleAsync(workspaceId, WorkspaceRole.Viewer, ct);

        return await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .Where(m => m.WorkspaceId == workspaceId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new WorkspaceMemberResponse(m.Id, m.WorkspaceId, m.UserId, m.Role, m.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<WorkspaceMemberResponse> AddMemberAsync(
        Guid workspaceId,
        AddWorkspaceMemberRequest request,
        CancellationToken ct)
    {
        var actorRole = await RequireRoleAsync(workspaceId, WorkspaceRole.Admin, ct);

        // An Admin cannot mint an Owner. Privilege escalation by invitation is still
        // privilege escalation.
        if (request.Role > actorRole)
        {
            throw new ForbiddenException($"You cannot grant a role above your own ({actorRole}).");
        }

        var existing = await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == request.UserId, ct);

        if (existing is not null)
        {
            throw new ConflictException("That user is already a member of this workspace.");
        }

        var member = new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = request.UserId,
            Role = request.Role
        };

        db.WorkspaceMembers.Add(member);

        audit.Record(
            "workspace.member.add",
            nameof(WorkspaceMember),
            member.Id.ToString(),
            metadataJson: $$"""{"userId":"{{request.UserId}}","role":"{{request.Role}}"}""");

        await db.SaveChangesAsync(ct);

        return new WorkspaceMemberResponse(member.Id, member.WorkspaceId, member.UserId, member.Role, member.CreatedAt);
    }

    /// <summary>
    /// Membership check that returns the caller's role, so callers get both the
    /// authorization decision and the value they need from one query.
    /// </summary>
    private async Task<WorkspaceRole> RequireRoleAsync(Guid workspaceId, WorkspaceRole minimum, CancellationToken ct)
    {
        var userId = RequireUser();

        var membership = await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct);

        if (membership is null)
        {
            throw new NotFoundException("Workspace", workspaceId);
        }

        if (membership.Role < minimum)
        {
            audit.Record(
                "workspace.access",
                nameof(Workspace),
                workspaceId.ToString(),
                allowed: false,
                denyReason: $"Requires {minimum}, caller has {membership.Role}.");

            throw new ForbiddenException($"This action requires {minimum} or above.");
        }

        return membership.Role;
    }

    private Guid RequireUser() => context.UserId ?? throw new UnauthenticatedException();

    private static WorkspaceResponse ToResponse(Workspace workspace, WorkspaceRole? role) =>
        new(workspace.Id,
            workspace.OrganizationId,
            workspace.Name,
            workspace.Slug,
            workspace.Description,
            workspace.IsActive,
            role,
            workspace.CreatedAt);
}
