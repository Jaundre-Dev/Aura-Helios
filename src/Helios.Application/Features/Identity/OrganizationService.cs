using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Contracts.Organizations;
using Helios.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Identity;

/// <summary>
/// Organizations sit above the workspace isolation boundary, so they carry no query
/// filter. Org-level roles arrive in a later phase; until then this is deliberately thin
/// and every workspace it parents does its own authorization.
/// </summary>
public sealed class OrganizationService(
    IHeliosDbContext db,
    IWorkspaceContext context,
    IAuditWriter audit)
{
    public async Task<IReadOnlyList<OrganizationResponse>> ListAsync(CancellationToken ct) =>
        await db.Organizations
            .OrderBy(o => o.Name)
            .Select(o => new OrganizationResponse(o.Id, o.Name, o.Slug, o.IsActive, o.CreatedAt))
            .ToListAsync(ct);

    public async Task<OrganizationResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == id, ct);

        return organization is null ? null : ToResponse(organization);
    }

    public async Task<OrganizationResponse> CreateAsync(CreateOrganizationRequest request, CancellationToken ct)
    {
        if (context.UserId is null)
        {
            throw new UnauthenticatedException();
        }

        var slug = Slug.From(request.Slug ?? request.Name);

        if (await db.Organizations.AnyAsync(o => o.Slug == slug, ct))
        {
            throw new ConflictException($"An organization with the slug '{slug}' already exists.");
        }

        var organization = new Organization
        {
            Name = request.Name.Trim(),
            Slug = slug
        };

        db.Organizations.Add(organization);

        audit.Record("organization.create", nameof(Organization), organization.Id.ToString());

        await db.SaveChangesAsync(ct);

        return ToResponse(organization);
    }

    private static OrganizationResponse ToResponse(Organization o) =>
        new(o.Id, o.Name, o.Slug, o.IsActive, o.CreatedAt);
}
