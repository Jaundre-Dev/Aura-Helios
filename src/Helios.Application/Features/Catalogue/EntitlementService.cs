using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Agreements;
using Helios.Application.Features.Identity;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Catalogue;

/// <summary>
/// Which products a company may use, per environment. Sandbox access to a callable product is
/// self-service and free. Live access additionally needs the product to be live-callable, and
/// special-personal-information products (biometrics) wait for platform approval with a stated
/// purpose. Enabling never starts a paid subscription.
/// </summary>
public sealed class EntitlementService(
    IHeliosDbContext db,
    OrganizationAccess access,
    CatalogueService catalogue,
    AgreementService agreements,
    IAuditWriter audit)
{
    public async Task<IReadOnlyList<EntitlementResponse>> ListAsync(Guid organizationId, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ViewOrganization, ct);

        return await (
            from entitlement in db.Entitlements.AsNoTracking()
            where entitlement.OrganizationId == organizationId
            join product in db.ApiProducts on entitlement.ProductId equals product.Id
            orderby product.Slug, entitlement.Environment
            select new EntitlementResponse(
                entitlement.Id, entitlement.OrganizationId, product.Slug, entitlement.Environment,
                entitlement.State, entitlement.Purpose, entitlement.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<EntitlementResponse> EnableAsync(
        Guid organizationId,
        EnableEntitlementRequest request,
        CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ManageEntitlements, ct);

        var product = await db.ApiProducts.SingleOrDefaultAsync(p => p.Slug == request.ProductSlug, ct)
            ?? throw new NotFoundException("Product", request.ProductSlug);

        if (!catalogue.IsCallable(product, request.Environment))
        {
            throw new ConflictException(
                $"'{product.Slug}' is {product.ReleaseState} and cannot be used in {request.Environment}.",
                "product_unavailable");
        }

        var live = request.Environment == ApiEnvironment.Live;
        var needsApproval = live && product.Sensitivity == Contracts.Catalogue.ProductSensitivity.SpecialPersonal;

        // Live use of anything that processes personal information records why (permitted purpose).
        if (live && product.Sensitivity != Contracts.Catalogue.ProductSensitivity.Standard && string.IsNullOrWhiteSpace(request.Purpose))
        {
            throw new ConflictException(
                $"Live use of '{product.Slug}' processes personal information; state the purpose it is used for.", "purpose_required");
        }

        // Live use needs the company's acceptance of the current terms and processing agreement.
        if (live)
        {
            await agreements.EnsureAcceptedAsync(organizationId, ct);
        }

        var entitlement = await db.Entitlements.SingleOrDefaultAsync(e =>
            e.OrganizationId == organizationId &&
            e.ProductId == product.Id &&
            e.Environment == request.Environment, ct);

        var isNew = entitlement is null;
        if (entitlement is null)
        {
            entitlement = new Entitlement
            {
                OrganizationId = organizationId,
                ProductId = product.Id,
                Environment = request.Environment
            };
            db.Entitlements.Add(entitlement);
        }

        // An enabled or pending entitlement is not reset by re-requesting it.
        if (isNew || entitlement.State == EntitlementState.Disabled)
        {
            entitlement.State = needsApproval ? EntitlementState.PendingApproval : EntitlementState.Enabled;
        }

        entitlement.Purpose = request.Purpose?.Trim() ?? entitlement.Purpose;

        audit.Record("entitlement.enable", nameof(Entitlement), entitlement.Id.ToString(),
            organizationId: organizationId,
            metadataJson: $$"""{"product":"{{product.Slug}}","environment":"{{request.Environment}}","state":"{{entitlement.State}}"}""");

        await db.SaveChangesAsync(ct);

        return ToResponse(entitlement, product.Slug);
    }

    public async Task DisableAsync(Guid organizationId, Guid entitlementId, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ManageEntitlements, ct);

        var entitlement = await db.Entitlements
            .SingleOrDefaultAsync(e => e.Id == entitlementId && e.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException("Entitlement", entitlementId);

        entitlement.State = EntitlementState.Disabled;

        audit.Record("entitlement.disable", nameof(Entitlement), entitlement.Id.ToString(),
            organizationId: organizationId);

        await db.SaveChangesAsync(ct);
    }

    private static EntitlementResponse ToResponse(Entitlement e, string slug) =>
        new(e.Id, e.OrganizationId, slug, e.Environment, e.State, e.Purpose, e.CreatedAt);
}
