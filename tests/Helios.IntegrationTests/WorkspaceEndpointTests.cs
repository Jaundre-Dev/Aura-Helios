using System.Net;
using System.Net.Http.Json;
using Helios.Contracts.Identity;
using Helios.Contracts.Organizations;
using Helios.Contracts.Projects;
using Helios.Contracts.Workspaces;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// Workspace and project endpoints against a real MySQL schema, driven by real accounts and
/// tokens. Every write leaves an audit row.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class WorkspaceEndpointTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    [Fact]
    public async Task Creating_a_workspace_makes_the_creator_its_owner()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();

        var response = await owner.Client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, "Platform Team"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var workspace = await response.Content.ReadFromJsonAsync<WorkspaceResponse>();

        Assert.NotNull(workspace);
        Assert.Equal("platform-team", workspace.Slug);
        Assert.Equal(WorkspaceRole.Owner, workspace.MyRole);
    }

    [Fact]
    public async Task A_user_only_sees_workspaces_they_belong_to()
    {
        var alice = await TestAccount.RegisterAsync(_factory);
        await alice.CreateOrganizationAsync();

        var bob = await TestAccount.RegisterAsync(_factory);
        var bobWorkspaces = await bob.Client.GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces");

        Assert.NotNull(bobWorkspaces);
        Assert.Empty(bobWorkspaces);
    }

    [Fact]
    public async Task A_stranger_gets_404_rather_than_403_for_someone_elses_workspace()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();
        var workspace = await owner.DefaultWorkspaceAsync(organization.Id);

        var stranger = await TestAccount.RegisterAsync(_factory);
        var response = await stranger.Client.GetAsync($"/api/v1/workspaces/{workspace.Id}");

        // 403 would confirm the workspace exists. 404 leaks nothing.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_slug_in_the_same_organization_is_rejected()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();

        var first = await owner.Client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, "Shared Name"));
        first.EnsureSuccessStatusCode();

        var second = await owner.Client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, "Shared Name"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Invalid_input_returns_validation_problem_details()
    {
        var user = await TestAccount.RegisterAsync(_factory);

        var response = await user.Client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(Guid.Empty, ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemShape>();

        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task An_admin_cannot_grant_a_role_above_their_own()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();
        var workspace = await owner.DefaultWorkspaceAsync(organization.Id);

        var admin = await TestAccount.RegisterAsync(_factory);
        var third = await TestAccount.RegisterAsync(_factory);

        foreach (var person in new[] { admin, third })
        {
            (await owner.Client.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/members",
                new AddOrganizationMemberRequest(person.Email, OrganizationRole.Operator))).EnsureSuccessStatusCode();
        }

        (await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members",
            new AddWorkspaceMemberRequest(admin.UserId, WorkspaceRole.Admin))).EnsureSuccessStatusCode();

        await admin.SelectWorkspaceAsync(workspace.Id);

        var escalate = await admin.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/members",
            new AddWorkspaceMemberRequest(third.UserId, WorkspaceRole.Owner));

        Assert.Equal(HttpStatusCode.Forbidden, escalate.StatusCode);
    }

    [Fact]
    public async Task Every_write_leaves_an_audit_row()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();

        var created = await owner.Client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, $"Audited {Guid.NewGuid():N}"));
        var workspace = (await created.Content.ReadFromJsonAsync<WorkspaceResponse>())!;

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();

        var entries = await db.AuditLogs
            .Where(a => a.ActorUserId == owner.UserId)
            .ToListAsync();

        Assert.Contains(entries, a =>
            a.Action == "organization.create" && a.Allowed && a.OrganizationId == organization.Id);
        Assert.Contains(entries, a =>
            a.Action == "workspace.create" &&
            a.ResourceId == workspace.Id.ToString() &&
            a.WorkspaceId == workspace.Id &&
            a.ResourceType == nameof(Domain.Identity.Workspace) &&
            !string.IsNullOrEmpty(a.CorrelationId));
    }

    [Fact]
    public async Task Projects_are_scoped_to_the_selected_workspace()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();
        var first = await owner.DefaultWorkspaceAsync(organization.Id);

        var secondCreated = await owner.Client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, $"Second {Guid.NewGuid():N}"));
        var second = (await secondCreated.Content.ReadFromJsonAsync<WorkspaceResponse>())!;

        await owner.SelectWorkspaceAsync(first.Id);
        var project = await owner.Client.PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest("Billing Service"));
        Assert.Equal(HttpStatusCode.Created, project.StatusCode);

        var firstProjects = await owner.Client.GetFromJsonAsync<List<ProjectResponse>>("/api/v1/projects");
        Assert.Single(firstProjects!);

        await owner.SelectWorkspaceAsync(second.Id);
        var secondProjects = await owner.Client.GetFromJsonAsync<List<ProjectResponse>>("/api/v1/projects");
        Assert.Empty(secondProjects!);
    }

    [Fact]
    public async Task Relaxing_a_projects_classification_is_recorded_separately()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();
        var workspace = await owner.DefaultWorkspaceAsync(organization.Id);
        await owner.SelectWorkspaceAsync(workspace.Id);

        var projectResponse = await owner.Client.PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest("Secret Service", Classification: Contracts.Common.DataClassification.Restricted));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectResponse>())!;

        var relax = await owner.Client.PatchAsJsonAsync($"/api/v1/projects/{project.Id}",
            new UpdateProjectRequest(Classification: Contracts.Common.DataClassification.Internal));
        relax.EnsureSuccessStatusCode();

        using var scope = _factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();

        var reclassification = await db.AuditLogs
            .SingleOrDefaultAsync(a => a.Action == "project.reclassify" && a.ResourceId == project.Id.ToString());

        Assert.NotNull(reclassification);
        Assert.Contains("Restricted", reclassification.Metadata);
        Assert.Contains("Internal", reclassification.Metadata);
    }

    [Fact]
    public async Task A_malformed_body_returns_400_not_500()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();
        await owner.SelectWorkspaceAsync((await owner.DefaultWorkspaceAsync(organization.Id)).Id);

        // classification is an enum; sending it as arbitrary text fails JSON binding. The
        // caller's mistake must surface as a 400, never as a server-fault 500.
        var body = new StringContent(
            """{"name":"Broken","classification":"not-a-classification"}""",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await owner.Client.PostAsync("/api/v1/projects", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record ValidationProblemShape(
        string? Title,
        Dictionary<string, string[]> Errors);
}
