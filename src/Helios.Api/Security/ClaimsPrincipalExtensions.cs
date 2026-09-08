using System.Security.Claims;
using Helios.Contracts.Identity;

namespace Helios.Api.Security;

/// <summary>
/// Reads HELIOS claims off the authenticated principal. Endpoints use these rather than
/// reaching for claim strings directly, so the parsing rules live in one place.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid? UserId(this ClaimsPrincipal principal) =>
        ReadGuid(principal, HeliosClaims.Subject);

    public static Guid? WorkspaceId(this ClaimsPrincipal principal) =>
        ReadGuid(principal, HeliosClaims.Workspace);

    public static WorkspaceRole? Role(this ClaimsPrincipal principal) =>
        Enum.TryParse<WorkspaceRole>(principal.FindFirstValue(HeliosClaims.Role), out var role)
            ? role
            : null;

    public static string? Email(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(HeliosClaims.Email);

    public static string? DisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(HeliosClaims.Name);

    private static Guid? ReadGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value) ? value : null;
}
