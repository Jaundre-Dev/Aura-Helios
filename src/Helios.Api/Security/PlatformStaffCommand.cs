using Helios.Application.Common;
using Helios.Application.Features.Platform;
using Helios.Contracts.Platform;

namespace Helios.Api.Security;

/// <summary>
/// Host console command that grants a platform role to an existing account:
/// <c>dotnet Helios.Api.dll platform-staff grant &lt;email&gt; &lt;Administrator|Finance|Support&gt;</c>.
/// It exists to create the first Administrator, who then manages staff through the API. It runs
/// with the host's own configuration and database access and is audited with source "console";
/// it is not reachable over HTTP.
/// </summary>
public sealed class PlatformStaffCommand
{
    private PlatformStaffCommand(string email, PlatformRole role)
    {
        Email = email;
        Role = role;
    }

    public string Email { get; }
    public PlatformRole Role { get; }

    /// <summary>
    /// Recognises the command and returns the remaining arguments for the host, so configuration
    /// switches (e.g. <c>--environment</c>) still apply.
    /// </summary>
    public static PlatformStaffCommand? TryParse(string[] args, out string[] hostArgs)
    {
        hostArgs = args;

        if (args.Length < 1 || args[0] != "platform-staff")
        {
            return null;
        }

        if (args.Length < 4 || args[1] != "grant" || !Enum.TryParse<PlatformRole>(args[3], ignoreCase: false, out var role) ||
            !Enum.IsDefined(role))
        {
            throw new ArgumentException(
                "Usage: platform-staff grant <email> <Administrator|Finance|Support>");
        }

        hostArgs = args[4..];
        return new PlatformStaffCommand(args[2], role);
    }

    public async Task<int> RunAsync(IServiceProvider services, TextWriter output)
    {
        using var scope = services.CreateScope();
        var staff = scope.ServiceProvider.GetRequiredService<PlatformStaffService>();

        try
        {
            var granted = await staff.GrantFromConsoleAsync(Email, Role, CancellationToken.None);
            await output.WriteLineAsync(
                $"Granted {granted.Role} to {granted.Email}. They must enrol an authenticator (POST /api/v1/platform/mfa/enrol) before platform access works.");
            return 0;
        }
        catch (NotFoundException)
        {
            await output.WriteLineAsync($"No active account with email '{Email}'. The person must register first.");
            return 1;
        }
    }
}
