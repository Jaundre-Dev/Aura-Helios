using Helios.Contracts.Catalogue;
using Helios.Domain.Common;

namespace Helios.Domain.ApiKeys;

/// <summary>
/// A scoped machine credential. Only a SHA-256 verifier of the secret is stored; the public id
/// finds the row and the verifier is compared in constant time. Owned by the company and pinned
/// to one workspace and environment, so a key can never act across those boundaries.
/// </summary>
public class ApiKey : AuditableEntity, IAggregateRoot
{
    public Guid OrganizationId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ProjectId { get; set; }

    public required string Name { get; set; }

    /// <summary>Random public identifier embedded in the key, unique across all keys.</summary>
    public required string PublicId { get; set; }

    /// <summary>The non-secret leading part shown in listings, e.g. <c>hk_test_abcd…</c>.</summary>
    public required string DisplayPrefix { get; set; }

    /// <summary>SHA-256 of the full key. The secret has 256 bits of entropy, so no stretching is needed.</summary>
    public required byte[] SecretHash { get; set; }

    public ApiEnvironment Environment { get; set; }

    /// <summary>Product slugs the key may call, comma separated.</summary>
    public required string Scopes { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Most this key may commit (reserved plus charged) per calendar month (UTC); null for no cap.</summary>
    public decimal? MonthlyBudget { get; set; }

    public IReadOnlyList<string> ScopeList =>
        Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool IsUsableAt(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
