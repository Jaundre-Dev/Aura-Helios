using System.Net.Http.Json;
using Helios.Contracts.ApiKeys;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.Contracts.Workspaces;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Helios.IntegrationTests.Fixtures;

/// <summary>
/// A company onboarded the way a customer would: owner signs up, creates the company, selects
/// the default workspace and enables the sandbox utility.
/// </summary>
public sealed class TestCompany
{
    public const string SaIdProduct = "identity.sa-id-validate";

    private readonly WebApplicationFactory<Program> _factory;

    public TestAccount Owner { get; }
    public OrganizationResponse Organization { get; }
    public WorkspaceResponse Workspace { get; }

    private TestCompany(WebApplicationFactory<Program> factory, TestAccount owner, OrganizationResponse organization, WorkspaceResponse workspace)
    {
        _factory = factory;
        Owner = owner;
        Organization = organization;
        Workspace = workspace;
    }

    public static async Task<TestCompany> OnboardAsync(WebApplicationFactory<Program> factory, bool enableSandbox = true)
    {
        var owner = await TestAccount.RegisterAsync(factory);
        var organization = await owner.CreateOrganizationAsync();
        var workspace = await owner.DefaultWorkspaceAsync(organization.Id);
        await owner.SelectWorkspaceAsync(workspace.Id);

        var company = new TestCompany(factory, owner, organization, workspace);

        if (enableSandbox)
        {
            (await owner.Client.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/entitlements",
                new EnableEntitlementRequest(SaIdProduct, ApiEnvironment.Sandbox))).EnsureSuccessStatusCode();
        }

        return company;
    }

    public async Task<CreatedApiKeyResponse> CreateKeyAsync(params string[] scopes)
    {
        var response = await Owner.Client.PostAsJsonAsync("/api/v1/api-keys",
            new CreateApiKeyRequest($"key {Guid.NewGuid():N}", ApiEnvironment.Sandbox,
                scopes.Length == 0 ? [SaIdProduct] : scopes));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedApiKeyResponse>())!;
    }

    public async Task EnableAsync(string slug, ApiEnvironment environment)
    {
        (await Owner.Client.PostAsJsonAsync($"/api/v1/organizations/{Organization.Id}/entitlements",
            new EnableEntitlementRequest(slug, environment))).EnsureSuccessStatusCode();
    }

    /// <summary>Enables the products for live and returns a live key scoped to them.</summary>
    public async Task<CreatedApiKeyResponse> CreateLiveKeyAsync(params string[] scopes)
    {
        foreach (var slug in scopes)
        {
            await EnableAsync(slug, ApiEnvironment.Live);
        }

        var response = await Owner.Client.PostAsJsonAsync("/api/v1/api-keys",
            new CreateApiKeyRequest($"live {Guid.NewGuid():N}", ApiEnvironment.Live, scopes));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedApiKeyResponse>())!;
    }

    /// <summary>A client authenticating only with the given API key.</summary>
    public HttpClient KeyClient(string secret)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", secret);
        return client;
    }

    /// <summary>Adds a new person in the given role and grants them the default workspace, signed in to it.</summary>
    public async Task<TestAccount> AddMemberAsync(OrganizationRole role)
    {
        var person = await TestAccount.RegisterAsync(_factory);

        (await Owner.Client.PostAsJsonAsync($"/api/v1/organizations/{Organization.Id}/members",
            new AddOrganizationMemberRequest(person.Email, role))).EnsureSuccessStatusCode();

        (await Owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{Workspace.Id}/members",
            new AddWorkspaceMemberRequest(person.UserId, Contracts.Identity.WorkspaceRole.Viewer))).EnsureSuccessStatusCode();

        await person.SelectWorkspaceAsync(Workspace.Id);
        return person;
    }
}
