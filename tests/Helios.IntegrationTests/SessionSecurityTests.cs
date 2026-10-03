using System.Net;
using System.Net.Http.Json;
using Helios.Api.Middleware;
using Helios.Contracts.Identity;
using Helios.Contracts.Organizations;
using Helios.Contracts.Workspaces;
using Helios.Infrastructure.Persistence.MySql;
using Helios.Infrastructure.Persistence.MySql.Identity;
using Helios.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests;

/// <summary>
/// P0/P1 regressions for HELIOS-REVIEW.md: login ignored lockout, and already-issued tokens kept
/// working after an account was disabled or access was revoked. Every test uses a real token.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class SessionSecurityTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    [Fact]
    public async Task A_locked_out_account_cannot_sign_in_even_with_the_right_password()
    {
        var account = await TestAccount.RegisterAsync(_factory);
        var client = _factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequest(account.Email, "WrongPassword123"));
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var correct = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(account.Email, TestAccount.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);

        using var scope = _factory.CreateSystemScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HeliosUser>>();
        var user = await users.FindByIdAsync(account.UserId.ToString());
        Assert.True(await users.IsLockedOutAsync(user!));

        var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.Action == "auth.login" && a.ResourceId == account.UserId.ToString() &&
            !a.Allowed && a.DenyReason == "locked_out"));
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_failure_count()
    {
        var account = await TestAccount.RegisterAsync(_factory);
        var client = _factory.CreateClient();

        for (var i = 0; i < 4; i++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, "WrongPassword123"));
        }

        (await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(account.Email, TestAccount.Password))).EnsureSuccessStatusCode();

        // Four more failures would lock a counter that was never reset.
        for (var i = 0; i < 4; i++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, "WrongPassword123"));
        }

        var again = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(account.Email, TestAccount.Password));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task A_disabled_account_is_refused_sign_in_and_its_existing_token_stops_working()
    {
        var account = await TestAccount.RegisterAsync(_factory);
        (await account.Client.GetAsync("/api/v1/auth/me")).EnsureSuccessStatusCode();

        await SetActiveAsync(account.UserId, isActive: false);

        var existing = await account.Client.GetAsync("/api/v1/auth/me");
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(account.Email, TestAccount.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, existing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Revoking_sessions_invalidates_every_existing_token()
    {
        var account = await TestAccount.RegisterAsync(_factory);
        var otherDevice = account.CloneWithCurrentToken();

        var revoke = await account.Client.PostAsync("/api/v1/auth/revoke-sessions", null);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await account.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/v1/auth/me")).StatusCode);

        // Signing in again issues a token under the new stamp, which works.
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(account.Email, TestAccount.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Removing_a_workspace_member_revokes_their_workspace_token()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var member = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();
        var workspace = await owner.DefaultWorkspaceAsync(organization.Id);

        (await owner.Client.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest(member.Email, OrganizationRole.Operator))).EnsureSuccessStatusCode();

        var grant = await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members",
            new AddWorkspaceMemberRequest(member.UserId, WorkspaceRole.Viewer));
        var grantResponse = (await grant.Content.ReadFromJsonAsync<WorkspaceMemberResponse>())!;

        await member.SelectWorkspaceAsync(workspace.Id);
        (await member.Client.GetAsync("/api/v1/projects")).EnsureSuccessStatusCode();

        var remove = await owner.Client.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/members/{grantResponse.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await member.Client.GetAsync("/api/v1/projects")).StatusCode);
    }

    [Fact]
    public async Task Removing_a_company_member_revokes_their_access_to_its_workspaces()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var member = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();
        var workspace = await owner.DefaultWorkspaceAsync(organization.Id);

        var added = await owner.Client.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/members",
            new AddOrganizationMemberRequest(member.Email, OrganizationRole.Operator));
        var membership = (await added.Content.ReadFromJsonAsync<OrganizationMemberResponse>())!;

        (await owner.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members",
            new AddWorkspaceMemberRequest(member.UserId, WorkspaceRole.Viewer))).EnsureSuccessStatusCode();

        await member.SelectWorkspaceAsync(workspace.Id);
        var workspaceToken = member.CloneWithCurrentToken();

        var remove = await owner.Client.DeleteAsync($"/api/v1/organizations/{organization.Id}/members/{membership.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await workspaceToken.GetAsync("/api/v1/projects")).StatusCode);

        // A fresh, workspace-less sign-in sees neither the company nor its workspaces.
        var fresh = await TestAccountSignIn(member.Email);
        Assert.Empty((await fresh.GetFromJsonAsync<List<OrganizationResponse>>("/api/v1/organizations"))!);
        Assert.Empty((await fresh.GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces"))!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await fresh.PostAsJsonAsync("/api/v1/auth/select-workspace", new SelectWorkspaceRequest(workspace.Id))).StatusCode);
    }

    [Fact]
    public async Task A_changed_workspace_role_invalidates_tokens_carrying_the_old_role()
    {
        var owner = await TestAccount.RegisterAsync(_factory);
        var organization = await owner.CreateOrganizationAsync();
        var workspace = await owner.DefaultWorkspaceAsync(organization.Id);
        await owner.SelectWorkspaceAsync(workspace.Id);

        using (var scope = _factory.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeliosDbContext>();
            var grant = await db.WorkspaceMembers.SingleAsync(m => m.WorkspaceId == workspace.Id && m.UserId == owner.UserId);
            grant.Role = WorkspaceRole.Viewer;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/v1/projects")).StatusCode);
    }

    [Fact]
    public async Task A_token_without_a_security_stamp_is_rejected()
    {
        var account = await TestAccount.RegisterAsync(_factory);

        // Signed with the right key, but shaped like a token issued before stamps existed.
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(HeliosApiFactory.SigningKey));
        var token = handler.WriteToken(new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "helios-test",
            audience: "helios-test",
            claims: [new System.Security.Claims.Claim("sub", account.UserId.ToString())],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256)));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Sign_in_is_throttled_per_client()
    {
        var throttled = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Helios:RateLimits:Auth:PermitLimit", "3"));
        var client = throttled.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequest(TestAccount.UniqueEmail(), "WrongPassword123"));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests],
            statuses);
    }

    [Fact]
    public async Task Responses_carry_a_request_id_that_appears_in_problem_details()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(RequestCorrelationMiddleware.HeaderName, "client-trace-0001");

        var response = await client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest(Guid.Empty, ""));

        Assert.Equal("client-trace-0001", response.Headers.GetValues(RequestCorrelationMiddleware.HeaderName).Single());

        // An unsafe caller-supplied id is replaced, never echoed.
        var unsafeClient = _factory.CreateClient();
        unsafeClient.DefaultRequestHeaders.TryAddWithoutValidation(RequestCorrelationMiddleware.HeaderName, "bad id\twith tab");
        var replaced = await unsafeClient.GetAsync("/health");
        Assert.StartsWith("req_", replaced.Headers.GetValues(RequestCorrelationMiddleware.HeaderName).Single());
    }

    private async Task<HttpClient> TestAccountSignIn(string email)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, TestAccount.Password));
        login.EnsureSuccessStatusCode();

        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        return client;
    }

    private async Task SetActiveAsync(Guid userId, bool isActive)
    {
        using var scope = _factory.CreateSystemScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HeliosUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        user!.IsActive = isActive;
        await users.UpdateAsync(user);
    }
}
