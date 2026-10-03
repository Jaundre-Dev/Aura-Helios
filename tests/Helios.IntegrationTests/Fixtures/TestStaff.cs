using System.Net.Http.Headers;
using System.Net.Http.Json;
using Helios.Application.Features.Platform;
using Helios.Contracts.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests.Fixtures;

/// <summary>
/// A platform staff member set up the way production does it: a registered account granted a role
/// through the console path, an enrolled authenticator, and a platform session obtained with a real
/// TOTP code. <see cref="Platform"/> carries that session; <see cref="Account"/> the ordinary one.
/// </summary>
public sealed class TestStaff
{
    private readonly HeliosApiFactory _factory;

    public TestAccount Account { get; }
    public PlatformRole Role { get; }
    public byte[] Secret { get; private set; } = [];
    public HttpClient Platform { get; }

    /// <summary>The TOTP step used for the latest accepted code; the next code must be later.</summary>
    public long LastStep { get; private set; }

    private TestStaff(HeliosApiFactory factory, TestAccount account, PlatformRole role)
    {
        _factory = factory;
        Account = account;
        Role = role;
        Platform = factory.CreateClient();
    }

    /// <summary>Registers, grants the role and enrols, without confirming the authenticator.</summary>
    public static async Task<TestStaff> EnrolledAsync(HeliosApiFactory factory, PlatformRole role)
    {
        var account = await TestAccount.RegisterAsync(factory);
        await GrantAsync(factory, account.Email, role);

        var staff = new TestStaff(factory, account, role);
        var enrolment = await account.Client.PostAsync("/api/v1/platform/mfa/enrol", null);
        enrolment.EnsureSuccessStatusCode();

        staff.Secret = Base32Decode((await enrolment.Content.ReadFromJsonAsync<MfaEnrolmentResponse>())!.Secret);
        return staff;
    }

    /// <summary>A staff member with a confirmed authenticator and a live platform session.</summary>
    public static async Task<TestStaff> CreateAsync(HeliosApiFactory factory, PlatformRole role)
    {
        var staff = await EnrolledAsync(factory, role);
        var step = Totp.StepAt(DateTimeOffset.UtcNow);

        var confirmed = await staff.Account.Client.PostAsJsonAsync("/api/v1/platform/mfa/confirm",
            new MfaCodeRequest(Totp.Code(staff.Secret, step)));
        confirmed.EnsureSuccessStatusCode();

        staff.LastStep = step;
        staff.Use((await confirmed.Content.ReadFromJsonAsync<PlatformSessionResponse>())!);
        return staff;
    }

    public static async Task GrantAsync(HeliosApiFactory factory, string email, PlatformRole role)
    {
        using var scope = factory.CreateSystemScope();
        await scope.ServiceProvider.GetRequiredService<PlatformStaffService>()
            .GrantFromConsoleAsync(email, role, CancellationToken.None);
    }

    /// <summary>The code for a given step offset from now (the server accepts ±1 step).</summary>
    public string CodeAt(long step) => Totp.Code(Secret, step);

    public void Use(PlatformSessionResponse session) =>
        Platform.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

    public static byte[] Base32Decode(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;

        foreach (var c in text.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;

            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
