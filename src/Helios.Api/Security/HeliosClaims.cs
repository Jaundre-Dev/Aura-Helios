namespace Helios.Api.Security;

/// <summary>
/// The claim names HELIOS issues and reads. Kept in one place because three things must
/// agree on them exactly: the token issuer that writes them, the JWT validation
/// parameters that name the subject and role claims, and <c>HttpWorkspaceContext</c> that
/// reads them back. Matches the plan (WP0.4): <c>sub</c>, <c>workspace_id</c>, <c>role</c>.
/// </summary>
public static class HeliosClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Workspace = "workspace_id";
    public const string Role = "role";
}
