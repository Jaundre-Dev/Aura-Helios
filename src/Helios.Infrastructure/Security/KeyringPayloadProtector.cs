using Helios.Application.Abstractions.Security;

namespace Helios.Infrastructure.Security;

/// <summary>AES-256-GCM sealing of job payloads with the same keyring that protects tenant secrets.</summary>
public sealed class KeyringPayloadProtector(SecretKeyring keyring) : IPayloadProtector
{
    public SealedPayload Protect(string plaintext)
    {
        var sealed_ = keyring.Protect(plaintext);
        return new SealedPayload(sealed_.KeyId, sealed_.Envelope);
    }

    public string Unprotect(string keyId, byte[] envelope) => keyring.Unprotect(keyId, envelope);
}
