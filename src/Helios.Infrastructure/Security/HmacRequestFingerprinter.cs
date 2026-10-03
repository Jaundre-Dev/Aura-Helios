using System.Security.Cryptography;
using System.Text;
using Helios.Application.Abstractions.Security;

namespace Helios.Infrastructure.Security;

/// <summary>HMAC-SHA256 of canonical request input under a subkey of the active secret key.</summary>
public sealed class HmacRequestFingerprinter(SecretKeyring keyring) : IRequestFingerprinter
{
    private const string Purpose = "helios/request-fingerprint/v1";

    public RequestFingerprint Compute(string canonicalInput) =>
        new(keyring.ActiveKeyId, Compute(keyring.ActiveKeyId, canonicalInput));

    public string Compute(string keyId, string canonicalInput)
    {
        var subkey = keyring.DeriveSubkey(keyId, Purpose);
        try
        {
            return Convert.ToHexStringLower(HMACSHA256.HashData(subkey, Encoding.UTF8.GetBytes(canonicalInput)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subkey);
        }
    }
}
