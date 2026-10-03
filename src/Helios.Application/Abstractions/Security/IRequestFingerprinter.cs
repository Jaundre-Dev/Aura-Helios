namespace Helios.Application.Abstractions.Security;

/// <summary>
/// Keyed fingerprints of request input for idempotency. A plain hash of a 13-digit ID number could
/// be reversed by enumeration; an HMAC under a server key cannot. The key id travels with the
/// fingerprint so a stored one can be recomputed under the same key after rotation.
/// </summary>
public interface IRequestFingerprinter
{
    RequestFingerprint Compute(string canonicalInput);

    string Compute(string keyId, string canonicalInput);
}

public readonly record struct RequestFingerprint(string KeyId, string Value);
