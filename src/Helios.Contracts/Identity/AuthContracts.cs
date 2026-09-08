namespace Helios.Contracts.Identity;

/// <summary>
/// Creates a local account. Registration is open in v1 because HELIOS is local-first;
/// gating it behind an invite or an org admin is a later hardening, not a Phase 0 concern.
/// </summary>
public sealed record RegisterRequest(string Email, string Password, string? DisplayName = null);

public sealed record LoginRequest(string Email, string Password);

/// <summary>
/// Selects the workspace a session acts in. The API re-issues a token carrying the
/// <c>workspace_id</c> and <c>role</c> claims, because a user signs in before choosing a
/// workspace and a token's claims cannot be edited in place.
/// </summary>
public sealed record SelectWorkspaceRequest(Guid WorkspaceId);

/// <summary>
/// The issued token and the identity it encodes, so a client need not decode the JWT to
/// know who it signed in as or which workspace the token is scoped to.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    AuthenticatedUser User);

public sealed record AuthenticatedUser(
    Guid Id,
    string Email,
    string? DisplayName,
    Guid? WorkspaceId,
    WorkspaceRole? Role);
