using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Contracts.Identity;
using Helios.Contracts.Projects;
using Helios.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Projects;

/// <summary>
/// Projects sit inside the caller's current workspace, so unlike workspaces these
/// queries rely on the global filter rather than working around it.
/// </summary>
public sealed class ProjectService(
    IHeliosDbContext db,
    IWorkspaceContext context,
    IAuditWriter audit)
{
    public async Task<IReadOnlyList<ProjectResponse>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        RequireWorkspace();

        var query = db.Projects.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        return await query
            .OrderBy(p => p.Name)
            .Select(p => ToResponse(p))
            .ToListAsync(ct);
    }

    public async Task<ProjectResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        RequireWorkspace();

        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == id, ct);

        return project is null ? null : ToResponse(project);
    }

    public async Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        await RequireRoleAsync(workspaceId, WorkspaceRole.Engineer, ct);

        var slug = Slug.From(request.Slug ?? request.Name);

        if (await db.Projects.AnyAsync(p => p.Slug == slug, ct))
        {
            throw new ConflictException($"A project with the slug '{slug}' already exists in this workspace.");
        }

        var project = new Project
        {
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description?.Trim(),
            Classification = request.Classification
        };

        db.Projects.Add(project);

        // Classification decides whether this project's code may ever reach a cloud model,
        // so the value it was created with is worth having in the trail.
        audit.Record(
            "project.create",
            nameof(Project),
            project.Id.ToString(),
            metadataJson: $$"""{"classification":"{{request.Classification}}"}""");

        await db.SaveChangesAsync(ct);

        return ToResponse(project);
    }

    public async Task<ProjectResponse> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        await RequireRoleAsync(workspaceId, WorkspaceRole.Engineer, ct);

        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Project", id);

        if (request.Name is { } name)
        {
            project.Name = name.Trim();
        }

        if (request.Description is { } description)
        {
            project.Description = description.Trim();
        }

        if (request.IsActive is { } isActive)
        {
            project.IsActive = isActive;
        }

        if (request.Classification is { } classification && classification != project.Classification)
        {
            // Relaxing classification widens where this project's data may be sent, so it
            // needs a higher bar than an ordinary edit and its own audit line.
            if (classification < project.Classification)
            {
                await RequireRoleAsync(workspaceId, WorkspaceRole.Admin, ct);
            }

            audit.Record(
                "project.reclassify",
                nameof(Project),
                id.ToString(),
                metadataJson: $$"""{"from":"{{project.Classification}}","to":"{{classification}}"}""");

            project.Classification = classification;
        }

        audit.Record("project.update", nameof(Project), id.ToString());

        await db.SaveChangesAsync(ct);

        return ToResponse(project);
    }

    /// <summary>
    /// Deactivates rather than deletes. Runs, findings and audit rows reference projects,
    /// and a hard delete would orphan the history that makes them explainable.
    /// </summary>
    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        await RequireRoleAsync(workspaceId, WorkspaceRole.Admin, ct);

        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Project", id);

        project.IsActive = false;

        audit.Record("project.deactivate", nameof(Project), id.ToString());

        await db.SaveChangesAsync(ct);
    }

    private async Task RequireRoleAsync(Guid workspaceId, WorkspaceRole minimum, CancellationToken ct)
    {
        var userId = context.UserId ?? throw new UnauthenticatedException();

        var membership = await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct)
            ?? throw new ForbiddenException("You are not a member of this workspace.");

        if (membership.Role < minimum)
        {
            audit.Record(
                "project.access",
                nameof(Project),
                allowed: false,
                denyReason: $"Requires {minimum}, caller has {membership.Role}.");

            throw new ForbiddenException($"This action requires {minimum} or above.");
        }
    }

    private Guid RequireWorkspace() =>
        context.WorkspaceId ?? throw new ForbiddenException("No workspace selected for this request.");

    private static ProjectResponse ToResponse(Project p) =>
        new(p.Id, p.WorkspaceId, p.Name, p.Slug, p.Description, p.Classification, p.IsActive, p.CreatedAt);
}
