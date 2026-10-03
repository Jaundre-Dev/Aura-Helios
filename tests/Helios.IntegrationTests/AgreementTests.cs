using System.Net;
using System.Net.Http.Json;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// Plan section 4: live use needs the Owner's acceptance of the current published terms and
/// processing agreement, and a stated purpose for products that process personal information.
/// Sandbox stays open.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class AgreementTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Extensions.TryGetValue("code", out var code) == true
            ? code?.ToString()
            : null;

    private static Task<HttpResponseMessage> EnableLiveAsync(TestCompany company, string slug, string? purpose = null) =>
        company.Owner.Client.PostAsJsonAsync($"/api/v1/organizations/{company.Organization.Id}/entitlements",
            new EnableEntitlementRequest(slug, ApiEnvironment.Live, purpose));

    [Fact]
    public async Task Live_use_waits_for_the_owner_to_accept_the_current_versions()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var admin = await company.AddMemberAsync(OrganizationRole.Admin);
        var url = $"/api/v1/organizations/{company.Organization.Id}/agreements";

        var blocked = await EnableLiveAsync(company, TestProducts.Metered);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("agreements_required", await CodeOf(blocked));

        var statuses = await company.Owner.Client.GetFromJsonAsync<List<AgreementStatusResponse>>(url);
        Assert.Equal(["dpa", "terms"], statuses!.Select(s => s.Document));
        Assert.All(statuses!, s => Assert.False(s.Accepted));

        // Only the Owner accepts, and only the current version.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.PostAsJsonAsync(url,
            new AcceptAgreementRequest("terms", HeliosApiFactory.TestTermsVersion))).StatusCode);
        var stale = await company.Owner.Client.PostAsJsonAsync(url, new AcceptAgreementRequest("terms", "test-terms-0"));
        Assert.Equal("agreement_version_mismatch", await CodeOf(stale));
        Assert.Equal(HttpStatusCode.NotFound, (await company.Owner.Client.PostAsJsonAsync(url,
            new AcceptAgreementRequest("marketing", "1"))).StatusCode);

        (await company.Owner.Client.PostAsJsonAsync(url, new AcceptAgreementRequest("terms", HeliosApiFactory.TestTermsVersion))).EnsureSuccessStatusCode();
        Assert.Equal("agreements_required", await CodeOf(await EnableLiveAsync(company, TestProducts.Metered)));

        // Accepting twice is harmless.
        (await company.Owner.Client.PostAsJsonAsync(url, new AcceptAgreementRequest("dpa", HeliosApiFactory.TestDpaVersion))).EnsureSuccessStatusCode();
        var accepted = await (await company.Owner.Client.PostAsJsonAsync(url, new AcceptAgreementRequest("dpa", HeliosApiFactory.TestDpaVersion)))
            .Content.ReadFromJsonAsync<List<AgreementStatusResponse>>();
        Assert.All(accepted!, s => Assert.Equal(company.Owner.UserId, s.AcceptedBy));

        Assert.Equal(HttpStatusCode.OK, (await EnableLiveAsync(company, TestProducts.Metered)).StatusCode);

        using var scope = _factory.CreateSystemScope();
        var rows = await scope.ServiceProvider.GetRequiredService<HeliosDbContext>().AgreementAcceptances
            .Where(a => a.OrganizationId == company.Organization.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(company.Owner.UserId, r.AcceptedBy));
    }

    [Fact]
    public async Task Sandbox_needs_no_agreements()
    {
        var company = await TestCompany.OnboardAsync(_factory, enableSandbox: false);

        Assert.Equal(HttpStatusCode.OK, (await company.Owner.Client.PostAsJsonAsync(
            $"/api/v1/organizations/{company.Organization.Id}/entitlements",
            new EnableEntitlementRequest(TestCompany.SaIdProduct, ApiEnvironment.Sandbox))).StatusCode);
    }

    [Fact]
    public async Task Personal_information_products_need_a_stated_purpose_for_live_use()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        await company.AcceptAgreementsAsync();

        await SetSensitivityAsync(TestProducts.Provider, ProductSensitivity.Personal);
        try
        {
            var noPurpose = await EnableLiveAsync(company, TestProducts.Provider);
            Assert.Equal(HttpStatusCode.Conflict, noPurpose.StatusCode);
            Assert.Equal("purpose_required", await CodeOf(noPurpose));

            var enabled = await EnableLiveAsync(company, TestProducts.Provider, "Supplier invoice capture for accounts payable");
            Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
            Assert.Equal("Supplier invoice capture for accounts payable",
                (await enabled.Content.ReadFromJsonAsync<EntitlementResponse>())!.Purpose);
        }
        finally
        {
            await SetSensitivityAsync(TestProducts.Provider, ProductSensitivity.Standard);
        }
    }

    [Fact]
    public async Task Without_published_documents_live_use_stays_closed()
    {
        var unpublished = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Helios:Legal:Documents:terms", "");
            builder.UseSetting("Helios:Legal:Documents:dpa", "");
        });

        var company = await TestCompany.OnboardAsync(unpublished);
        var response = await EnableLiveAsync(company, TestProducts.Metered);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("agreements_not_published", await CodeOf(response));
    }

    private async Task SetSensitivityAsync(string slug, ProductSensitivity sensitivity)
    {
        using var scope = _factory.CreateSystemScope();
        await scope.ServiceProvider.GetRequiredService<HeliosDbContext>().ApiProducts
            .Where(p => p.Slug == slug)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Sensitivity, sensitivity));
    }
}
