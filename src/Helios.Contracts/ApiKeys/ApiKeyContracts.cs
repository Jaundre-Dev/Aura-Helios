using Helios.Contracts.Catalogue;

namespace Helios.Contracts.ApiKeys;

/// <summary>
/// Creates a key in the caller's current workspace. <see cref="Scopes"/> lists the product slugs
/// the key may call; it must be non-empty and every product must be enabled for the environment.
/// </summary>
public sealed record CreateApiKeyRequest(
    string Name,
    ApiEnvironment Environment,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt = null,
    Guid? ProjectId = null);

public sealed record ApiKeyResponse(
    Guid Id,
    Guid OrganizationId,
    Guid WorkspaceId,
    Guid? ProjectId,
    string Name,
    string Prefix,
    ApiEnvironment Environment,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// Returned exactly once, at creation or rotation. HELIOS stores only a hash; a lost secret
/// cannot be recovered, only replaced.
/// </summary>
public sealed record CreatedApiKeyResponse(ApiKeyResponse Key, string Secret);
