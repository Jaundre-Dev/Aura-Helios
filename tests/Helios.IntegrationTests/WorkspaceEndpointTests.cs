using System.Net;
using System.Net.Http.Json;
using Helios.Contracts.Identity;
using Helios.Contracts.Organizations;
using Helios.Contracts.Projects;
using Helios.Contracts.Workspaces;
using Helios.Domain.Platform;
using Helios.Infrastructure.Persistence.MySql;
using Helios.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// Exercises the real endpoints against a real MySQL schema — WP0.5, and Gate 0 check 4
/// (every write leaves an audit row).
/// </summary>
public sealed class WorkspaceEndpointTests(HeliosApiFactory factory)
    : IClassFixture<HeliosApiFactory>
{
    private readonly HeliosApiFactory _factory = factory;

    private HttpClient SignedInAs(Guid userId, Guid? workspaceId = null)
    {
        _factory.Context.UserId = userId;
        _factory.Context.WorkspaceId = workspaceId;
        _factory.Context.IsSystem = false;

        return _factory.CreateClient();
    }

    private async Task<OrganizationResponse> CreateOrganizationAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/organizations",
            new CreateOrganizationRequest(name));

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<OrganizationResponse>())!;
    }

    [Fact]
    public async Task Creating_a_workspace_makes_the_creator_its_owner()
    {
        var user = Guid.CreateVersion7();
        var client = SignedInAs(user);
        var organization = await CreateOrganizationAsync(client, $"Acme {Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync("/api/v1/workspaces",
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
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();

        var aliceClient = SignedInAs(alice);
        var organization = await CreateOrganizationAsync(aliceClient, $"Isolation {Guid.NewGuid():N}");

        var created = await aliceClient.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, $"Alice Space {Guid.NewGuid():N}"));
        created.EnsureSuccessStatusCode();

        var bobClient = SignedInAs(bob);
        var bobWorkspaces = await bobClient.GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces");

        Assert.NotNull(bobWorkspaces);
        Assert.Empty(bobWorkspaces);
    }

    [Fact]
    public async Task A_stranger_gets_404_rather_than_403_for_someone_elses_workspace()
    {
        var owner = Guid.CreateVersion7();
        var ownerClient = SignedInAs(owner);
        var organization = await CreateOrganizationAsync(ownerClient, $"Private {Guid.NewGuid():N}");

        var created = await ownerClient.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, $"Private Space {Guid.NewGuid():N}"));
        var workspace = (await created.Content.ReadFromJsonAsync<WorkspaceResponse>())!;

        var strangerClient = SignedInAs(Guid.CreateVersion7());
        var response = await strangerClient.GetAsync($"/api/v1/workspaces/{workspace.Id}");

        // 403 would confirm the workspace exists. 404 leaks nothing.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_slug_in_the_same_organization_is_rejected()
    {
        var user = Guid.CreateVersion7();
        var client = SignedInAs(user);
        var organization = await CreateOrganizationAsync(client, $"Dupes {Guid.NewGuid():N}");

        var first = await client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, "Shared Name"));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, "Shared Name"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Invalid_input_returns_validation_problem_details()
    {
        var client = SignedInAs(Guid.CreateVersion7());

        var response = await client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(Guid.Empty, ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemShape>();

        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task An_admin_cannot_grant_a_role_above_their_own()
    {
        var owner = Guid.CreateVersion7();
        var ownerClient = SignedInAs(owner);
        var organization = await CreateOrganizationAsync(ownerClient, $"Escalation {Guid.NewGuid():N}");

        var created = await ownerClient.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, $"Escalation {Guid.NewGuid():N}"));
        var workspace = (await created.Content.ReadFromJsonAsync<WorkspaceResponse>())!;

        var admin = Guid.CreateVersion7();
        var promote = await ownerClient.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/members",
            new AddWorkspaceMemberRequest(admin, WorkspaceRole.Admin));
        promote.EnsureSuccessStatusCode();

        var adminClient = SignedInAs(admin, workspace.Id);
        var escalate = await adminClient.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/members",
            new AddWorkspaceMemberRequest(Guid.CreateVersion7(), WorkspaceRole.Owner));

        Assert.Equal(HttpStatusCode.Forbidden, escalate.StatusCode);
    }

    [Fact]
    public async Task Every_write_leaves_an_audit_row()
    {
        var user = Guid.CreateVersion7();
        var client = SignedInAs(user);
        var organization = await CreateOrganizationAsync(client, $"Audited {Guid.NewGuid():N}");

        var created = await client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, $"Audited {Guid.NewGuid():N}"));
        var workspace = (await created.Content.ReadFromJsonAsync<WorkspaceResponse>())!;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();

        var entries = await db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.ActorUserId == user)
            .ToListAsync();

        Assert.Contains(entries, a => a.Action == "organization.create" && a.Allowed);
        Assert.Contains(entries, a =>
            a.Action == "workspace.create" &&
            a.ResourceId == workspace.Id.ToString() &&
            a.ResourceType == nameof(Domain.Identity.Workspace));
    }

    [Fact]
    public async Task Projects_are_scoped_to_the_selected_workspace()
    {
        var user = Guid.CreateVersion7();
        var client = SignedInAs(user);
        var organization = await CreateOrganizationAsync(client, $"Scoped {Guid.NewGuid():N}");

        var firstCreated = await client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, $"First {Guid.NewGuid():N}"));
        var first = (await firstCreated.Content.ReadFromJsonAsync<WorkspaceResponse>())!;

        var secondCreated = await client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, $"Second {Guid.NewGuid():N}"));
        var second = (await secondCreated.Content.ReadFromJsonAsync<WorkspaceResponse>())!;

        var inFirst = SignedInAs(user, first.Id);
        var project = await inFirst.PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest("Billing Service"));
        Assert.Equal(HttpStatusCode.Created, project.StatusCode);

        var firstProjects = await inFirst.GetFromJsonAsync<List<ProjectResponse>>("/api/v1/projects");
        Assert.Single(firstProjects!);

        var inSecond = SignedInAs(user, second.Id);
        var secondProjects = await inSecond.GetFromJsonAsync<List<ProjectResponse>>("/api/v1/projects");
        Assert.Empty(secondProjects!);
    }

    [Fact]
    public async Task Relaxing_a_projects_classification_is_recorded_separately()
    {
        var user = Guid.CreateVersion7();
        var client = SignedInAs(user);
        var organization = await CreateOrganizationAsync(client, $"Classify {Guid.NewGuid():N}");

        var created = await client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(organization.Id, $"Classify {Guid.NewGuid():N}"));
        var workspace = (await created.Content.ReadFromJsonAsync<WorkspaceResponse>())!;

        var scoped = SignedInAs(user, workspace.Id);
        var projectResponse = await scoped.PostAsJsonAsync("/api/v1/projects",
            new CreateProjectRequest("Secret Service", Classification: Contracts.Common.DataClassification.Restricted));
        var project = (await projectResponse.Content.ReadFromJsonAsync<ProjectResponse>())!;

        var relax = await scoped.PatchAsJsonAsync($"/api/v1/projects/{project.Id}",
            new UpdateProjectRequest(Classification: Contracts.Common.DataClassification.Internal));
        relax.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();

        var reclassification = await db.AuditLogs
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(a => a.Action == "project.reclassify" && a.ResourceId == project.Id.ToString());

        Assert.NotNull(reclassification);
        Assert.Contains("Restricted", reclassification.Metadata);
        Assert.Contains("Internal", reclassification.Metadata);
    }

    private sealed record ValidationProblemShape(
        string? Title,
        Dictionary<string, string[]> Errors);
}
