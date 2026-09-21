using Helios.Domain.Common;

namespace Helios.Domain.Platform;

/// <summary>
/// A workspace-scoped secret — a provider credential, a webhook signing key. The plaintext is
/// never stored: only the AES-GCM ciphertext, the nonce it was sealed with, the authentication
/// tag and the id of the master key that wrapped it. Keeping the key id means a later rotation
/// can re-wrap without the plaintext ever leaving the process, and a decrypt can still find the
/// key a given row was sealed under.
///
/// Isolation is by <see cref="WorkspaceId"/>, enforced by the same global query filter as every
/// other tenant-scoped aggregate, so one workspace can never read another's secret even under
/// the same <see cref="Reference"/>.
/// </summary>
public class Secret : AuditableEntity, IAggregateRoot
{
    public Guid WorkspaceId { get; set; }

    /// <summary>The lookup name, unique within a workspace (e.g. <c>provider:openai:apikey</c>).</summary>
    public required string Reference { get; set; }

    /// <summary>
    /// The sealed envelope: the 12-byte AES-GCM nonce, then the ciphertext, then the 16-byte
    /// authentication tag, in one column. Stored as a single blob so no field is ever the fixed
    /// 16-byte width the MySQL driver reads back as a GUID, and the whole thing (min 28 bytes)
    /// stays tamper-evident as one unit.
    /// </summary>
    public required byte[] Envelope { get; set; }

    /// <summary>Which master key sealed this row, so rotation can add a new key without a re-encrypt.</summary>
    public required string KeyId { get; set; }
}
