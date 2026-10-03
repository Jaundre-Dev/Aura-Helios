using System.Net;
using System.Net.Http.Json;
using Helios.Contracts.Billing;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.IntegrationTests.Fixtures;

namespace Helios.IntegrationTests;

/// <summary>
/// The catalogue must be honest: only what is implemented is callable, partner products stay
/// unavailable, and nothing is callable in live before billing exists.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class CatalogueAndEntitlementTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    [Fact]
    public async Task The_catalogue_lists_the_initial_twelve_with_only_the_id_utility_callable_in_sandbox()
    {
        var listed = await _factory.CreateClient()
            .GetFromJsonAsync<List<ProductSummaryResponse>>("/api/v1/catalogue");

        // test.* products exist only in the integration test database.
        var products = listed!.Where(p => !p.Slug.StartsWith("test.", StringComparison.Ordinal)).ToList();
        Assert.Equal(12, products.Count);

        var callable = Assert.Single(products, p => p.CallableInSandbox);
        Assert.Equal(TestCompany.SaIdProduct, callable.Slug);
        Assert.Equal(ProductReleaseState.Sandbox, callable.ReleaseState);

        Assert.DoesNotContain(products, p => p.CallableInLive);
        Assert.All(products.Where(p => p.Slug != TestCompany.SaIdProduct),
            p => Assert.Equal(ProductReleaseState.Planned, p.ReleaseState));
        Assert.All(products.Where(p => p.Delivery == ProductDelivery.Partner),
            p => Assert.False(p.CallableInSandbox));
    }

    [Fact]
    public async Task Product_detail_publishes_the_request_schema_and_limitations()
    {
        var detail = await _factory.CreateClient()
            .GetFromJsonAsync<ProductDetailResponse>($"/api/v1/catalogue/{TestCompany.SaIdProduct}");

        Assert.NotNull(detail);
        var version = Assert.Single(detail.Versions);
        Assert.Equal("1", version.Version);
        Assert.Contains("idNumber", version.RequestSchemaJson);
        Assert.Contains("not a Home Affairs verification", detail.Product.Limitations);
    }

    [Fact]
    public async Task A_planned_product_cannot_be_enabled()
    {
        var company = await TestCompany.OnboardAsync(_factory, enableSandbox: false);

        var response = await company.Owner.Client.PostAsJsonAsync(
            $"/api/v1/organizations/{company.Organization.Id}/entitlements",
            new EnableEntitlementRequest("identity.face-compare", ApiEnvironment.Sandbox));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("product_unavailable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_id_utility_cannot_be_enabled_for_live_yet()
    {
        var company = await TestCompany.OnboardAsync(_factory, enableSandbox: false);

        var response = await company.Owner.Client.PostAsJsonAsync(
            $"/api/v1/organizations/{company.Organization.Id}/entitlements",
            new EnableEntitlementRequest(TestCompany.SaIdProduct, ApiEnvironment.Live));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Enabling_sandbox_is_idempotent_and_listed()
    {
        var company = await TestCompany.OnboardAsync(_factory);

        (await company.Owner.Client.PostAsJsonAsync(
            $"/api/v1/organizations/{company.Organization.Id}/entitlements",
            new EnableEntitlementRequest(TestCompany.SaIdProduct, ApiEnvironment.Sandbox))).EnsureSuccessStatusCode();

        var entitlements = await company.Owner.Client.GetFromJsonAsync<List<EntitlementResponse>>(
            $"/api/v1/organizations/{company.Organization.Id}/entitlements");

        var only = Assert.Single(entitlements!);
        Assert.Equal(EntitlementState.Enabled, only.State);
        Assert.Equal(ApiEnvironment.Sandbox, only.Environment);
    }

    [Fact]
    public async Task Another_company_cannot_read_or_change_entitlements()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var stranger = await TestAccount.RegisterAsync(_factory);

        var read = await stranger.Client.GetAsync($"/api/v1/organizations/{company.Organization.Id}/entitlements");
        var write = await stranger.Client.PostAsJsonAsync(
            $"/api/v1/organizations/{company.Organization.Id}/entitlements",
            new EnableEntitlementRequest(TestCompany.SaIdProduct, ApiEnvironment.Sandbox));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    [Fact]
    public async Task Finance_manages_the_billing_profile_and_a_developer_cannot_see_it()
    {
        var company = await TestCompany.OnboardAsync(_factory);
        var finance = await company.AddMemberAsync(OrganizationRole.Finance);
        var developer = await company.AddMemberAsync(OrganizationRole.Developer);
        var url = $"/api/v1/organizations/{company.Organization.Id}/billing-profile";

        var profile = new UpsertBillingProfileRequest(
            "Example Trading (Pty) Ltd", "accounts@example.test", "1 Example Street", "Cape Town", "8001",
            VatNumber: "4123456789");

        var saved = await finance.Client.PutAsJsonAsync(url, profile);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await saved.Content.ReadFromJsonAsync<BillingProfileResponse>();
        Assert.Equal("ZAR", body!.Currency);

        Assert.Equal(HttpStatusCode.Forbidden, (await developer.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.Client.PutAsJsonAsync(url, profile)).StatusCode);
    }

    [Fact]
    public async Task A_malformed_vat_number_is_rejected()
    {
        var company = await TestCompany.OnboardAsync(_factory);

        var response = await company.Owner.Client.PutAsJsonAsync(
            $"/api/v1/organizations/{company.Organization.Id}/billing-profile",
            new UpsertBillingProfileRequest("Example (Pty) Ltd", "a@example.test", "1 Street", "Durban", "4001",
                VatNumber: "123"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
