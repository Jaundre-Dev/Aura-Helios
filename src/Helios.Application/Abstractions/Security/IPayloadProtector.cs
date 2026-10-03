namespace Helios.Application.Abstractions.Security;

/// <summary>
/// Authenticated encryption for request payloads that must survive until a job finishes — a worker
/// may need to re-run or reconcile after a restart. Sealed under the server keyring and purged when
/// the job reaches a terminal state.
/// </summary>
public interface IPayloadProtector
{
    SealedPayload Protect(string plaintext);

    string Unprotect(string keyId, byte[] envelope);
}

public readonly record struct SealedPayload(string KeyId, byte[] Envelope);
