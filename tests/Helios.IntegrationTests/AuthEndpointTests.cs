using System.Net;
using System.Net.Http.Json;
using Helios.Contracts.Identity;
using Helios.IntegrationTests.Fixtures;

namespace Helios.IntegrationTests;

/// <summary>
/// Sign-in behaviour (WP0.4): registration, credential checks, and the authorization gate
/// on the REST surface. Token signing and validation are covered by
/// <see cref="JwtTokenIssuerTests"/>; these tests are about the endpoints' behaviour.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class AuthEndpointTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    private const string GoodPassword = "Sup3rSecretPass";

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@helios.test";

    [Fact]
    public async Task Register_issues_a_token_for_the_new_account()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, GoodPassword, "Ada Lovelace"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.Equal(email, auth.User.Email);
        Assert.Null(auth.User.WorkspaceId);   // no workspace chosen yet
    }

    [Fact]
    public async Task Login_with_the_right_password_returns_a_token()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();

        await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, GoodPassword));

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(email, GoodPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
    }

    [Fact]
    public async Task A_weak_password_is_rejected_with_the_identity_rules()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(UniqueEmail(), "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_same_email_cannot_register_twice()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();

        var first = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, GoodPassword));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, GoodPassword));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_unauthorized()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail();

        await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, GoodPassword));

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(email, "WrongPassword123"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_an_unknown_email_is_unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(UniqueEmail(), GoodPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_protected_endpoint_rejects_an_unauthenticated_caller()
    {
        // No user on the context — the test auth handler returns no result, so the
        // RequireAuthorization gate challenges with 401.
        _factory.Context.UserId = null;
        _factory.Context.WorkspaceId = null;

        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
