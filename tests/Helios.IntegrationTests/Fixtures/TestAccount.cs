using System.Net.Http.Headers;
using System.Net.Http.Json;
using Helios.Contracts.Identity;
using Helios.Contracts.Organizations;
using Helios.Contracts.Workspaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests.Fixtures;

/// <summary>
/// A real registered account driving the API with real JWTs, so every test passes through the
/// same authentication and session checks production does.
/// </summary>
public sealed class TestAccount
{
    public const string Password = "Sup3rSecretPass";

    private readonly WebApplicationFactory<Program> _factory;

    public Guid UserId { get; private set; }
    public string Email { get; }
    public string Token { get; private set; } = string.Empty;
    public HttpClient Client { get; }

    private TestAccount(WebApplicationFactory<Program> factory, string email)
    {
        _factory = factory;
        Email = email;
        Client = factory.CreateClient();
    }

    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@helios.test";

    public static async Task<TestAccount> RegisterAsync(WebApplicationFactory<Program> factory, string? displayName = null)
    {
        var account = new TestAccount(factory, UniqueEmail());

        var response = await account.Client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(account.Email, Password, displayName));
        response.EnsureSuccessStatusCode();

        account.Use((await response.Content.ReadFromJsonAsync<AuthResponse>())!);

        // Verify the address the way a person would: follow the emailed link.
        if (factory.Services.GetService<Application.Abstractions.Messaging.IEmailSender>() is RecordingEmailSender mailbox &&
            mailbox.LatestLink(account.Email, "verify-email") is { } link)
        {
            (await account.Client.PostAsJsonAsync("/api/v1/auth/verify-email",
                new VerifyEmailRequest(link.UserId, link.Token))).EnsureSuccessStatusCode();
        }

        return account;
    }

    public async Task<OrganizationResponse> CreateOrganizationAsync(string? name = null)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/organizations",
            new CreateOrganizationRequest(name ?? $"Company {Guid.NewGuid():N}"));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<OrganizationResponse>())!;
    }

    public async Task<WorkspaceResponse> DefaultWorkspaceAsync(Guid organizationId)
    {
        var workspaces = await Client.GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces");
        return workspaces!.Single(w => w.OrganizationId == organizationId && w.Slug == "default");
    }

    /// <summary>Re-issues the token scoped to a workspace and uses it from now on.</summary>
    public async Task SelectWorkspaceAsync(Guid workspaceId)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/select-workspace",
            new SelectWorkspaceRequest(workspaceId));
        response.EnsureSuccessStatusCode();

        Use((await response.Content.ReadFromJsonAsync<AuthResponse>())!);
    }

    /// <summary>A second client holding this account's current token, unaffected by later re-issues.</summary>
    public HttpClient CloneWithCurrentToken()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    private void Use(AuthResponse auth)
    {
        UserId = auth.User.Id;
        Token = auth.AccessToken;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }
}
