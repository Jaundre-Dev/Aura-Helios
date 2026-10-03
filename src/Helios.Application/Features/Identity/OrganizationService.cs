using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Contracts.Identity;
using Helios.Contracts.Organizations;
using Helios.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Identity;

/// <summary>
/// Customer companies and their teams. Every read and write requires an active membership of an
/// active organisation; a non-member sees 404 so the endpoint never confirms that another
/// company exists. Creating a company makes the caller its Owner and provisions its default
/// workspace in the same transaction, so there is never a company nobody can administer.
/// </summary>
public sealed class OrganizationService(
    IHeliosDbContext db,
    IUnitOfWork unitOfWork,
    OrganizationAccess access,
    IUserDirectory users,
    IAuditWriter audit)
{
    public const string DefaultWorkspaceSlug = "default";

    public async Task<IReadOnlyList<OrganizationResponse>> ListAsync(CancellationToken ct)
    {
        var userId = access.RequireUser();

        var query =
            from member in db.OrganizationMembers
            where member.UserId == userId && member.IsActive
            join organization in db.Organizations on member.OrganizationId equals organization.Id
            where organization.IsActive
            orderby organization.Name
            select new OrganizationResponse(
                organization.Id,
                organization.Name,
                organization.Slug,
                organization.IsActive,
                organization.CreatedAt,
                member.Role);

        return await query.ToListAsync(ct);
    }

    public async Task<OrganizationResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        OrganizationMember membership;
        try
        {
            membership = await access.RequireAsync(id, OrganizationPermission.ViewOrganization, ct);
        }
        catch (NotFoundException)
        {
            return null;
        }

        var organization = await db.Organizations.SingleAsync(o => o.Id == id, ct);

        return ToResponse(organization, membership.Role);
    }

    public async Task<OrganizationResponse> CreateAsync(CreateOrganizationRequest request, CancellationToken ct)
    {
        var userId = access.RequireUser();
        var slug = Slug.From(request.Slug ?? request.Name);

        // Plan section 4: sign up → verify email → create the company. The Owner of a billing
        // customer must be reachable at a proven address.
        var person = (await users.GetAsync([userId], ct)).GetValueOrDefault(userId);
        if (person is not { EmailVerified: true })
        {
            throw new ForbiddenException("Verify your email address before creating a company.", "email_not_verified");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (await db.Organizations.AnyAsync(o => o.Slug == slug, token))
            {
                throw new ConflictException($"An organization with the slug '{slug}' already exists.");
            }

            var organization = new Organization
            {
                Name = request.Name.Trim(),
                Slug = slug
            };

            var owner = new OrganizationMember
            {
                OrganizationId = organization.Id,
                UserId = userId,
                Role = OrganizationRole.Owner
            };

            var workspace = new Workspace
            {
                OrganizationId = organization.Id,
                Name = "Default",
                Slug = DefaultWorkspaceSlug,
                Description = "Default workspace for API requests, keys and uploads."
            };

            var workspaceOwner = new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = userId,
                Role = WorkspaceRole.Owner
            };

            db.Organizations.Add(organization);
            db.OrganizationMembers.Add(owner);
            db.Workspaces.Add(workspace);
            db.WorkspaceMembers.Add(workspaceOwner);

            audit.Record("organization.create", nameof(Organization), organization.Id.ToString(),
                organizationId: organization.Id);
            audit.Record("workspace.create", nameof(Workspace), workspace.Id.ToString(),
                organizationId: organization.Id, workspaceId: workspace.Id,
                metadataJson: """{"default":true}""");

            return ToResponse(organization, OrganizationRole.Owner);
        }, ct);
    }

    public async Task<IReadOnlyList<OrganizationMemberResponse>> ListMembersAsync(Guid organizationId, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ViewOrganization, ct);

        var members = await db.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.IsActive)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var people = await users.GetAsync(members.Select(m => m.UserId).ToArray(), ct);

        return members.Select(m => ToResponse(m, people.GetValueOrDefault(m.UserId))).ToList();
    }

    public async Task<OrganizationMemberResponse> AddMemberAsync(
        Guid organizationId,
        AddOrganizationMemberRequest request,
        CancellationToken ct)
    {
        // Permission first, so a caller who may not manage the team learns nothing about
        // which email addresses have accounts. Repeated under lock inside the transaction.
        await access.RequireAsync(organizationId, OrganizationPermission.ManageTeam, ct);

        var person = await users.FindActiveByEmailAsync(request.Email, ct)
            // The email is not echoed: the message reaches logs, which must not collect addresses.
            ?? throw new NotFoundException("Account", "for the supplied email");

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var actor = await access.RequireLockedAsync(organizationId, OrganizationPermission.ManageTeam, token);
            RequireMayAssign(actor, request.Role);

            var existing = await db.OrganizationMembers
                .SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == person.Id, token);

            if (existing is { IsActive: true })
            {
                throw new ConflictException("That user is already a member of this organization.");
            }

            var member = existing ?? new OrganizationMember { OrganizationId = organizationId, UserId = person.Id };
            member.Role = request.Role;
            member.IsActive = true;

            if (existing is null)
            {
                db.OrganizationMembers.Add(member);
            }

            audit.Record("organization.member.add", nameof(OrganizationMember), member.Id.ToString(),
                organizationId: organizationId,
                metadataJson: $$"""{"userId":"{{person.Id}}","role":"{{request.Role}}"}""");

            return ToResponse(member, person);
        }, ct);
    }

    public async Task<OrganizationMemberResponse> UpdateMemberAsync(
        Guid organizationId,
        Guid memberId,
        UpdateOrganizationMemberRequest request,
        CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ManageTeam, ct);

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var actor = await access.RequireLockedAsync(organizationId, OrganizationPermission.ManageTeam, token);

            var member = await db.OrganizationMembers
                .SingleOrDefaultAsync(m => m.Id == memberId && m.OrganizationId == organizationId && m.IsActive, token)
                ?? throw new NotFoundException("Member", memberId);

            // Changing an Owner, or making someone one, is an Owner decision.
            RequireMayAssign(actor, member.Role);
            RequireMayAssign(actor, request.Role);

            if (member.Role == OrganizationRole.Owner && request.Role != OrganizationRole.Owner)
            {
                await RequireAnotherOwnerAsync(organizationId, member.Id, token);
            }

            audit.Record("organization.member.update", nameof(OrganizationMember), member.Id.ToString(),
                organizationId: organizationId,
                metadataJson: $$"""{"from":"{{member.Role}}","to":"{{request.Role}}"}""");

            member.Role = request.Role;

            var person = (await users.GetAsync([member.UserId], token)).GetValueOrDefault(member.UserId);
            return ToResponse(member, person);
        }, ct);
    }

    /// <summary>
    /// Deactivates the membership and removes the person from every workspace of this company in
    /// the same transaction. Tokens they already hold stop working on the next request, because
    /// the session check reads membership from the database rather than trusting token claims.
    /// </summary>
    public async Task RemoveMemberAsync(Guid organizationId, Guid memberId, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ManageTeam, ct);

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var actor = await access.RequireLockedAsync(organizationId, OrganizationPermission.ManageTeam, token);

            var member = await db.OrganizationMembers
                .SingleOrDefaultAsync(m => m.Id == memberId && m.OrganizationId == organizationId && m.IsActive, token)
                ?? throw new NotFoundException("Member", memberId);

            RequireMayAssign(actor, member.Role);

            if (member.Role == OrganizationRole.Owner)
            {
                await RequireAnotherOwnerAsync(organizationId, member.Id, token);
            }

            member.IsActive = false;

            var workspaceIds = db.Workspaces.IgnoreQueryFilters()
                .Where(w => w.OrganizationId == organizationId)
                .Select(w => w.Id);

            var grants = await db.WorkspaceMembers.IgnoreQueryFilters()
                .Where(m => m.UserId == member.UserId && workspaceIds.Contains(m.WorkspaceId))
                .ToListAsync(token);

            db.WorkspaceMembers.RemoveRange(grants);

            audit.Record("organization.member.remove", nameof(OrganizationMember), member.Id.ToString(),
                organizationId: organizationId,
                metadataJson: $$"""{"userId":"{{member.UserId}}","workspaceGrantsRemoved":{{grants.Count}}}""");

            return true;
        }, ct);
    }

    private static void RequireMayAssign(OrganizationMember actor, OrganizationRole role)
    {
        if (role == OrganizationRole.Owner && actor.Role != OrganizationRole.Owner)
        {
            throw new ForbiddenException("Only an Owner can grant, change or remove the Owner role.");
        }
    }

    private async Task RequireAnotherOwnerAsync(Guid organizationId, Guid exceptMemberId, CancellationToken ct)
    {
        var others = await db.OrganizationMembers.CountAsync(m =>
            m.OrganizationId == organizationId &&
            m.Id != exceptMemberId &&
            m.IsActive &&
            m.Role == OrganizationRole.Owner, ct);

        if (others == 0)
        {
            throw new ConflictException("An organization must keep at least one Owner.");
        }
    }

    private static OrganizationResponse ToResponse(Organization o, OrganizationRole? role) =>
        new(o.Id, o.Name, o.Slug, o.IsActive, o.CreatedAt, role);

    private static OrganizationMemberResponse ToResponse(OrganizationMember m, UserSummary? person) =>
        new(m.Id, m.OrganizationId, m.UserId, person?.Email, person?.DisplayName, m.Role, m.IsActive, m.CreatedAt);
}
