using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Helios.Application.Features.Platform;
using Helios.Contracts.Identity;
using Helios.Contracts.Organizations;
using Helios.Contracts.Platform;
using Helios.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc;

namespace Helios.IntegrationTests;

/// <summary>
/// Email verification, password reset and two-step sign-in: links work once and expire, nothing
/// reveals whether an account exists, a reset signs the account out everywhere, the password alone
/// never yields a session once an authenticator is on, and codes (including recovery codes) work once.
/// </summary>
[Collection(HeliosApiCollection.Name)]
public sealed class AccountSecurityTests(HeliosApiFactory factory)
{
    private readonly HeliosApiFactory _factory = factory;

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Extensions.TryGetValue("code", out var code) == true
            ? code?.ToString()
            : null;

    private async Task<(HttpClient Client, string Email, Guid UserId)> RegisterUnverifiedAsync()
    {
        var client = _factory.CreateClient();
        var email = TestAccount.UniqueEmail();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, TestAccount.Password));
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, email, auth.User.Id);
    }

    [Fact]
    public async Task A_company_needs_a_verified_email_and_each_link_works_once()
    {
        var (client, email, userId) = await RegisterUnverifiedAsync();

        var blocked = await client.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest($"Co {Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("email_not_verified", await CodeOf(blocked));

        var first = _factory.Emails.LatestLink(email, "verify-email")!.Value;
        Assert.Equal(userId, first.UserId);

        // Asking again replaces the link: the first one stops working.
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/v1/auth/resend-verification", null)).StatusCode);
        var second = _factory.Emails.LatestLink(email, "verify-email")!.Value;
        Assert.NotEqual(first.Token, second.Token);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new VerifyEmailRequest(userId, first.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new VerifyEmailRequest(Guid.NewGuid(), second.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new VerifyEmailRequest(userId, second.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new VerifyEmailRequest(userId, second.Token))).StatusCode);

        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest($"Co {Guid.NewGuid():N}"))).StatusCode);

        var status = await client.GetFromJsonAsync<AccountSecurityResponse>("/api/v1/auth/security");
        Assert.True(status!.EmailVerified);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/v1/auth/resend-verification", null)).StatusCode);
    }

    [Fact]
    public async Task A_password_reset_works_once_signs_out_everywhere_and_lifts_a_lockout()
    {
        var account = await TestAccount.RegisterAsync(_factory);
        var anonymous = _factory.CreateClient();
        var oldSession = account.CloneWithCurrentToken();

        // Lock the account with bad passwords.
        for (var i = 0; i < 5; i++)
        {
            await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, "Wrong-password-123"));
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, TestAccount.Password))).StatusCode);

        // The same answer for an unknown address, and no mail for it.
        var before = _factory.Emails.Sent.Count;
        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostAsJsonAsync("/api/v1/auth/forgot-password",
            new ForgotPasswordRequest($"nobody-{Guid.NewGuid():N}@helios.test"))).StatusCode);
        Assert.Equal(before, _factory.Emails.Sent.Count);

        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostAsJsonAsync("/api/v1/auth/forgot-password",
            new ForgotPasswordRequest(account.Email))).StatusCode);
        var link = _factory.Emails.LatestLink(account.Email, "reset-password")!.Value;

        // A weak password is refused without spending the link.
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest(link.UserId, link.Token, "short"))).StatusCode);

        const string newPassword = "Brand-New-Passw0rd";
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest(link.UserId, link.Token, newPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest(link.UserId, link.Token, "Another-Passw0rd-1"))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, TestAccount.Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, newPassword))).StatusCode);
    }

    private static async Task<(byte[] Secret, IReadOnlyList<string> RecoveryCodes, long Step)> TurnOnMfaAsync(TestAccount account)
    {
        var setup = await (await account.Client.PostAsync("/api/v1/auth/mfa/enrol", null)).Content.ReadFromJsonAsync<MfaSetupResponse>();
        var secret = TestStaff.Base32Decode(setup!.Secret);
        var step = Totp.StepAt(DateTimeOffset.UtcNow);

        var confirmed = await account.Client.PostAsJsonAsync("/api/v1/auth/mfa/confirm", new AccountCodeRequest(Totp.Code(secret, step)));
        confirmed.EnsureSuccessStatusCode();
        var codes = (await confirmed.Content.ReadFromJsonAsync<RecoveryCodesResponse>())!.RecoveryCodes;
        return (secret, codes, step);
    }

    [Fact]
    public async Task With_an_authenticator_the_password_alone_never_yields_a_session()
    {
        var account = await TestAccount.RegisterAsync(_factory);
        var (secret, recovery, step) = await TurnOnMfaAsync(account);
        Assert.Equal(10, recovery.Count);
        Assert.Equal(10, recovery.Distinct().Count());

        var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, TestAccount.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", body);
        var challenge = JsonSerializer.Deserialize<MfaChallengeResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(challenge.MfaRequired);

        // The challenge is not a bearer token.
        var misuse = _factory.CreateClient();
        misuse.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", challenge.MfaToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await misuse.GetAsync("/api/v1/auth/me")).StatusCode);

        // The confirmation code cannot be replayed; the next step's code works.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/login",
            new MfaLoginRequest(challenge.MfaToken, Totp.Code(secret, step)))).StatusCode);
        var session = await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/login", new MfaLoginRequest(challenge.MfaToken, Totp.Code(secret, step + 1)));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.False(string.IsNullOrEmpty((await session.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken));

        // A recovery code works exactly once.
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/login", new MfaLoginRequest(challenge.MfaToken, recovery[0]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/login", new MfaLoginRequest(challenge.MfaToken, recovery[0]))).StatusCode);

        var status = await account.Client.GetFromJsonAsync<AccountSecurityResponse>("/api/v1/auth/security");
        Assert.True(status!.MfaEnabled);
        Assert.Equal(9, status.RecoveryCodesRemaining);

        // A challenge from before "sign out everywhere" is dead.
        await account.Client.PostAsync("/api/v1/auth/revoke-sessions", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/login",
            new MfaLoginRequest(challenge.MfaToken, recovery[1]))).StatusCode);
    }

    [Fact]
    public async Task Bad_codes_lock_the_second_factor_and_turning_it_off_needs_a_code()
    {
        var account = await TestAccount.RegisterAsync(_factory);
        var (secret, _, step) = await TurnOnMfaAsync(account);
        var anonymous = _factory.CreateClient();

        var challenge = (await (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Email, TestAccount.Password)))
            .Content.ReadFromJsonAsync<MfaChallengeResponse>())!;

        var valid = new HashSet<string> { Totp.Code(secret, step - 1), Totp.Code(secret, step), Totp.Code(secret, step + 1), Totp.Code(secret, step + 2) };
        var wrong = Enumerable.Range(0, 1000).Select(i => i.ToString("D6")).First(c => !valid.Contains(c));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/login", new MfaLoginRequest(challenge.MfaToken, wrong))).StatusCode);
        }

        var locked = await anonymous.PostAsJsonAsync("/api/v1/auth/mfa/login", new MfaLoginRequest(challenge.MfaToken, Totp.Code(secret, step + 1)));
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
        Assert.Equal("mfa_locked", await CodeOf(locked));

        var other = await TestAccount.RegisterAsync(_factory);
        var (otherSecret, otherRecovery, otherStep) = await TurnOnMfaAsync(other);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.PostAsJsonAsync("/api/v1/auth/mfa/disable", new AccountCodeRequest(wrong))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.Client.PostAsJsonAsync("/api/v1/auth/mfa/disable", new AccountCodeRequest(otherRecovery[0]))).StatusCode);

        var plain = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(other.Email, TestAccount.Password));
        Assert.False(string.IsNullOrEmpty((await plain.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken));
        _ = (otherSecret, otherStep);
    }

    [Fact]
    public async Task Platform_recovery_codes_stand_in_once_and_die_with_an_mfa_reset()
    {
        var admin = await TestStaff.CreateAsync(_factory, PlatformRole.Administrator);
        var staff = await TestStaff.CreateAsync(_factory, PlatformRole.Support);

        var codes = (await (await staff.Platform.PostAsync("/api/v1/platform/recovery-codes", null))
            .Content.ReadFromJsonAsync<RecoveryCodesResponse>())!.RecoveryCodes;

        Assert.Equal(HttpStatusCode.OK, (await staff.Account.Client.PostAsJsonAsync("/api/v1/platform/mfa/verify", new MfaCodeRequest(codes[0]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Account.Client.PostAsJsonAsync("/api/v1/platform/mfa/verify", new MfaCodeRequest(codes[0]))).StatusCode);

        (await admin.Platform.PostAsync($"/api/v1/platform/staff/{staff.Account.UserId}/reset-mfa", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Account.Client.PostAsJsonAsync("/api/v1/platform/mfa/verify", new MfaCodeRequest(codes[1]))).StatusCode);
    }
}
