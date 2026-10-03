using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Features.Identity;
using Helios.Contracts.Billing;
using Helios.Contracts.Organizations;
using Helios.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Billing;

/// <summary>
/// Who HELIOS invoices for a company. Readable with <c>ViewBilling</c>, changeable with
/// <c>ManageBilling</c> (Owner and Finance) — Admins and Developers can do neither by default.
/// </summary>
public sealed class BillingProfileService(
    IHeliosDbContext db,
    OrganizationAccess access,
    IAuditWriter audit)
{
    public async Task<BillingProfileResponse?> GetAsync(Guid organizationId, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ViewBilling, ct);

        var profile = await db.BillingProfiles.AsNoTracking()
            .SingleOrDefaultAsync(b => b.OrganizationId == organizationId, ct);

        return profile is null ? null : ToResponse(profile);
    }

    public async Task<BillingProfileResponse> UpsertAsync(
        Guid organizationId,
        UpsertBillingProfileRequest request,
        CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ManageBilling, ct);

        var profile = await db.BillingProfiles.SingleOrDefaultAsync(b => b.OrganizationId == organizationId, ct);
        var created = profile is null;

        profile ??= new BillingProfile
        {
            OrganizationId = organizationId,
            LegalName = string.Empty,
            BillingEmail = string.Empty,
            AddressLine1 = string.Empty,
            City = string.Empty,
            PostalCode = string.Empty,
            CountryCode = "ZA"
        };

        profile.LegalName = request.LegalName.Trim();
        profile.BillingEmail = request.BillingEmail.Trim();
        profile.AddressLine1 = request.AddressLine1.Trim();
        profile.AddressLine2 = Clean(request.AddressLine2);
        profile.City = request.City.Trim();
        profile.Province = Clean(request.Province);
        profile.PostalCode = request.PostalCode.Trim();
        profile.CountryCode = request.CountryCode.Trim().ToUpperInvariant();
        profile.RegistrationNumber = Clean(request.RegistrationNumber);
        profile.VatNumber = Clean(request.VatNumber);

        if (created)
        {
            db.BillingProfiles.Add(profile);
        }

        // Field values are not logged: they include contact details.
        audit.Record(created ? "billing_profile.create" : "billing_profile.update",
            nameof(BillingProfile), profile.Id.ToString(), organizationId: organizationId);

        await db.SaveChangesAsync(ct);

        return ToResponse(profile);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BillingProfileResponse ToResponse(BillingProfile b) =>
        new(b.OrganizationId, b.LegalName, b.BillingEmail, b.AddressLine1, b.AddressLine2, b.City, b.Province,
            b.PostalCode, b.CountryCode, b.RegistrationNumber, b.VatNumber, b.Currency, b.UpdatedAt ?? b.CreatedAt);
}
