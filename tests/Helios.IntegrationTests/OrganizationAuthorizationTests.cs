using System.Net;
using System.Net.Http.Json;
using Helios.Contracts.Identity;
using Helios.Contracts.Organizations;
using Helios.Contracts.Workspaces;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// P0 regressions for HELIOS-REVIEW.md: organisation list/detail had no membership filter, and
/// workspace creation only checked that the organisation existed. Each test here is one of
/// those holes, shown closed.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class OrganizationAuthorizationTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    [Fact]
    public async Task Listing_organizations_returns_only_the_callers_own()
    {
        var alice = await TestAccount.RegisterAsync(_factory);
        var bob = await TestAccount.RegisterAsync(_factory);

        var aliceOrg = await alice.CreateOrganizationAsync();
        var bobOrg = await bob.CreateOrganizationAsync();

        var aliceSees = await alice.Client.GetFromJsonAsync<List<OrganizationResponse>>("/api/v1/organizations");

        Assert.NotNull(aliceSees);
        var only = Assert.Single(aliceSees);
        Assert.Equal(aliceOrg.Id, only.Id);
        Assert.Equal(OrganizationRole.Owner, only.MyRole);
        Assert.DoesNotContain(aliceSees, o => o.Id == bobOrg.Id);
    }

    [Fact]
    public async Task Reading_another_companys_organization_returns_404()
    {
        var alice = await TestAccount.RegisterAsync(_factory);
        var mallory = await TestAccount.RegisterAsync(_factory);
        var organization = await alice.CreateOrganizationAsync();

        var response = await mallory.Client.GetAsync($"/api/v1/organizations/{organization.Id}");
        var members = await mallory.Client.GetAsync($"/api/v1/organizations/{organization.Id}/members");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, members.StatusCode);
    }

    [Fact]
    public async Task Creating_a_workspace_under_another_company_is_refused_and_creates_nothing()
    {
        var alice = await TestAccount.RegisterAsync(_factory);
        var mallory = await TestAccount.RegisterAsync(_factory);
        var organization = await alice.CreateOrganizationAsync();

        var response = await mallory.Client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, "Hijack", "hijack"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.False(await db.Workspaces.AnyAsync(w => w.OrganizationId == organization.Id && w.Slug == "hijack"));
        Assert.False(await db.WorkspaceMembers.AnyAsync(m => m.UserId == mallory.UserId));
    }

    [Fact]
    public async Task A_member_without_workspace_permission_cannot_create_workspaces()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var finance = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();

        (await owner.Client.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest(finance.Email, OrganizationRole.Finance))).EnsureSuccessStatusCode();

        var response = await finance.Client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, "Finance Space"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // The denial is persisted, not lost with the failed request.
        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.ActorUserId == finance.UserId &&
            a.Action == "organization.access" &&
            !a.Allowed &&
            a.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task Creating_a_company_makes_the_caller_owner_with_a_default_workspace()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();

        var members = await owner.Client.GetFromJsonAsync<List<OrganizationMemberResponse>>(
            $"/api/v1/organizations/{organization.Id}/members");
        var workspace = await owner.DefaultWorkspaceAsync(organization.Id);

        var member = Assert.Single(members!);
        Assert.Equal(owner.UserId, member.UserId);
        Assert.Equal(OrganizationRole.Owner, member.Role);
        Assert.Equal(owner.Email, member.Email);
        Assert.Equal(WorkspaceRole.Owner, workspace.MyRole);
    }

    [Fact]
    public async Task An_admin_cannot_grant_the_owner_role()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var admin = await TestAccount.RegisterAsync(_factory);
        var other = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();

        (await owner.Client.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest(admin.Email, OrganizationRole.Admin))).EnsureSuccessStatusCode();

        var response = await admin.Client.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest(other.Email, OrganizationRole.Owner));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_developer_cannot_manage_the_team()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var developer = await TestAccount.RegisterAsync(_factory);
        var other = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();

        (await owner.Client.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest(developer.Email, OrganizationRole.Developer))).EnsureSuccessStatusCode();

        var response = await developer.Client.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest(other.Email, OrganizationRole.Operator));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_last_owner_cannot_be_removed_or_demoted()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();

        var members = await owner.Client.GetFromJsonAsync<List<OrganizationMemberResponse>>(
            $"/api/v1/organizations/{organization.Id}/members");
        var self = Assert.Single(members!);

        var remove = await owner.Client.DeleteAsync($"/api/v1/organizations/{organization.Id}/members/{self.Id}");
        var demote = await owner.Client.PatchAsJsonAsync($"/api/v1/organizations/{organization.Id}/members/{self.Id}",
            new UpdateOrganizationMemberRequest(OrganizationRole.Admin));

        Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
    }

    [Fact]
    public async Task A_workspace_cannot_be_granted_to_someone_outside_its_company()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var outsider = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();
        var workspace = await owner.DefaultWorkspaceAsync(organization.Id);

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members",
            new AddWorkspaceMemberRequest(outsider.UserId, WorkspaceRole.Viewer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Adding_an_unknown_email_returns_404()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest(TestAccount.UniqueEmail(), OrganizationRole.Operator));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
