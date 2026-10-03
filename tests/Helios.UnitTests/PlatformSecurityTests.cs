using System.Text;
using Helios.Application.Features.Platform;
using Helios.Contracts.Platform;
using Helios.Domain.Platform;

namespace Helios.UnitTests;

/// <summary>
/// TOTP against the RFC 6238 reference vectors (SHA-1 secret "12345678901234567890"; the RFC
/// prints 8 digits, authenticator apps use the last 6), and the platform role map's separations.
/// </summary>
public class PlatformSecurityTests
{
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Codes_match_the_RFC_6238_vectors(long unixSeconds, string expected)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
        Assert.Equal(expected, Totp.Code(RfcSecret, step));
    }

    [Fact]
    public void Verify_accepts_the_adjacent_steps_only_and_reports_the_matched_step()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = Totp.StepAt(now);

        Assert.Equal(step, Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step), now));
        Assert.Equal(step - 1, Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 1), now));
        Assert.Equal(step + 1, Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step + 1), now));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 2), now));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step + 2), now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Malformed_codes_are_refused(string code)
    {
        Assert.Null(Totp.Verify(RfcSecret, code, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Base32_matches_RFC_4648_and_the_uri_carries_it()
    {
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.Base32(RfcSecret));
        Assert.Equal("MZXW6YQ", Totp.Base32("foob"u8.ToArray()));

        var uri = Totp.OtpAuthUri("AURA HELIOS", "staff@helios.test", RfcSecret);
        Assert.StartsWith("otpauth://totp/AURA%20HELIOS:staff%40helios.test?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    [Fact]
    public void Administrator_holds_every_platform_permission()
    {
        Assert.All(Enum.GetValues<PlatformPermission>(),
            p => Assert.True(PlatformPermissions.Grants(PlatformRole.Administrator, p)));
    }

    [Fact]
    public void Support_cannot_touch_money_staff_approvals_or_the_catalogue()
    {
        foreach (var permission in new[]
                 {
                     PlatformPermission.ManageStaff, PlatformPermission.AdjustCredit, PlatformPermission.PublishPrices,
                     PlatformPermission.ApproveEntitlements, PlatformPermission.ManageCatalogue,
                     PlatformPermission.ResolveRequests, PlatformPermission.ViewPayments, PlatformPermission.ViewAudit
                 })
        {
            Assert.False(PlatformPermissions.Grants(PlatformRole.Support, permission), permission.ToString());
        }

        Assert.True(PlatformPermissions.Grants(PlatformRole.Support, PlatformPermission.ViewOperations));
        Assert.True(PlatformPermissions.Grants(PlatformRole.Support, PlatformPermission.RetryDeliveries));
    }

    [Fact]
    public void Finance_handles_money_but_not_staff_approvals_or_the_catalogue()
    {
        Assert.True(PlatformPermissions.Grants(PlatformRole.Finance, PlatformPermission.AdjustCredit));
        Assert.True(PlatformPermissions.Grants(PlatformRole.Finance, PlatformPermission.PublishPrices));
        Assert.False(PlatformPermissions.Grants(PlatformRole.Finance, PlatformPermission.ManageStaff));
        Assert.False(PlatformPermissions.Grants(PlatformRole.Finance, PlatformPermission.ApproveEntitlements));
        Assert.False(PlatformPermissions.Grants(PlatformRole.Finance, PlatformPermission.ManageCatalogue));
    }
}
