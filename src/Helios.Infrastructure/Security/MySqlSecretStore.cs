using Helios.Application.Abstractions.Security;
using Helios.Domain.Platform;
using Helios.Infrastructure.Persistence.MySql;
using Microsoft.EntityFrameworkCore;

namespace Helios.Infrastructure.Security;

/// <summary>
/// The server-managed secret store. Values are sealed with AES-256-GCM by the
/// <see cref="SecretKeyring"/> before they touch the database, so the row carries only ciphertext;
/// the plaintext never lands in a column, a log line or an audit entry.
///
/// Every secret belongs to a workspace and is reached only through the current
/// <see cref="IWorkspaceContext"/> — the same tenant boundary the rest of the system uses — so one
/// workspace can neither read nor overwrite another's secret, even under an identical reference.
/// A secret is never resolvable without a workspace, so the store refuses a system-scoped caller
/// rather than guessing which tenant a bare reference belongs to.
/// </summary>
public sealed class MySqlSecretStore(
    HeliosDbContext db,
    IWorkspaceContext workspaceContext,
    SecretKeyring keyring,
    IAuditWriter audit) : ISecretStore
{
    public async Task<string?> GetAsync(string reference, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        RequireWorkspace();

        // The global query filter already restricts this to the current workspace, so a reference
        // that exists only in another tenant simply returns nothing.
        var secret = await db.Secrets
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Reference == reference, cancellationToken);

        return secret is null
            ? null
            : keyring.Unprotect(secret.KeyId, secret.Envelope);
    }

    public async Task SetAsync(string reference, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(value);
        var workspaceId = RequireWorkspace();

        var sealed_ = keyring.Protect(value);

        var existing = await db.Secrets
            .FirstOrDefaultAsync(s => s.Reference == reference, cancellationToken);

        if (existing is null)
        {
            db.Secrets.Add(new Secret
            {
                WorkspaceId = workspaceId,
                Reference = reference,
                KeyId = sealed_.KeyId,
                Envelope = sealed_.Envelope
            });
        }
        else
        {
            existing.KeyId = sealed_.KeyId;
            existing.Envelope = sealed_.Envelope;
        }

        // The reference and the wrapping key are safe to record; the value is not, and never is.
        audit.Record(
            action: "secret.set",
            resourceType: nameof(Secret),
            resourceId: reference,
            metadataJson: $"{{\"reference\":\"{reference}\",\"keyId\":\"{sealed_.KeyId}\",\"created\":{(existing is null).ToString().ToLowerInvariant()}}}");

        await db.SaveChangesAsync(cancellationToken);
    }

    private Guid RequireWorkspace()
    {
        if (workspaceContext.IsSystem || workspaceContext.WorkspaceId is not { } workspaceId)
        {
            throw new InvalidOperationException(
                "Secrets are workspace-scoped; there is no workspace on the current context to resolve one against.");
        }

        return workspaceId;
    }
}
