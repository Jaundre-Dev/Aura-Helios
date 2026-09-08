using System.Security.Claims;
using Helios.Api.Security;
using Helios.Application.Abstractions.Security;

namespace Helios.Api.Configuration;

/// <summary>
/// Resolves the acting user and workspace from the current request's claims. Registered
/// scoped, and read by the global query filters and the audit interceptor.
/// </summary>
/// <remarks>
/// <see cref="IsSystem"/> is hardcoded to false and must stay that way. It is the switch
/// that bypasses workspace isolation, and nothing reachable from an HTTP request may flip
/// it — only the worker and migrations use <c>SystemWorkspaceContext</c>.
/// <para>
/// Until WP0.4 lands there are no claims to read, so both values return null and every
/// workspace-filtered query returns nothing. That is the correct failure direction:
/// unauthenticated sees empty, never everything.
/// </para>
/// </remarks>
public sealed class HttpWorkspaceContext(IHttpContextAccessor accessor) : IWorkspaceContext
{
    public Guid? UserId => ReadGuid(HeliosClaims.Subject);

    public Guid? WorkspaceId => ReadGuid(HeliosClaims.Workspace);

    public bool IsSystem => false;

    public string? IpAddress =>
        accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private Guid? ReadGuid(string claimType)
    {
        var value = accessor.HttpContext?.User.FindFirstValue(claimType);

        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}
