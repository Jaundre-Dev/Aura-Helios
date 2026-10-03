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

    /// <summary>
    /// The account's security stamp when the token was issued. Rotating the stamp (password
    /// change, "sign out everywhere") invalidates every token carrying the old value.
    /// </summary>
    public const string SecurityStamp = "sst";

    /// <summary>Set only on API-key principals: the authenticating key's id.</summary>
    public const string ApiKey = "api_key_id";

    /// <summary>Set only on API-key principals: the key's owning company.</summary>
    public const string Organization = "organization_id";

    /// <summary>Set only on API-key principals: <c>Sandbox</c> or <c>Live</c>.</summary>
    public const string Environment = "environment";

    /// <summary>
    /// Set only on a platform step-up session: the staff role it was issued under. The session
    /// validator compares it with the staff record on every request.
    /// </summary>
    public const string PlatformRole = "platform_role";

    /// <summary>Authentication methods. <c>mfa</c> on platform sessions, proven with an authenticator code.</summary>
    public const string AuthenticationMethod = "amr";
}
