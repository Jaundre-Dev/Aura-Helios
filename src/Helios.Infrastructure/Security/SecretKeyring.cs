using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Helios.Infrastructure.Security;

/// <summary>
/// The server-managed master keys that wrap workspace secrets, and the AES-256-GCM sealing they
/// perform. Keys are configured, never generated at random on boot: a random key would silently
/// orphan every stored secret on the next restart. The active key seals new writes; every key
/// (active and retired) stays available for decrypt, so a rotation adds a key and switches the
/// active id without touching a single stored row.
///
/// Configuration (kept out of appsettings.json so a real key is never committed):
///   Helios:Secrets:ActiveKeyId = k1
///   Helios:Secrets:Keys:k1     = &lt;base64 of 32 random bytes&gt;
/// </summary>
public sealed class SecretKeyring
{
    private const int KeySizeBytes = 32;                 // AES-256
    private static readonly int NonceSize = AesGcm.NonceByteSizes.MaxSize; // 12
    private static readonly int TagSize = AesGcm.TagByteSizes.MaxSize;     // 16

    private readonly IReadOnlyDictionary<string, byte[]> _keys;

    public string ActiveKeyId { get; }

    public SecretKeyring(string activeKeyId, IReadOnlyDictionary<string, byte[]> keys)
    {
        if (string.IsNullOrWhiteSpace(activeKeyId))
        {
            throw new ArgumentException("An active secret key id is required.", nameof(activeKeyId));
        }

        if (keys.Count == 0)
        {
            throw new ArgumentException("At least one secret key must be configured.", nameof(keys));
        }

        foreach (var (id, key) in keys)
        {
            if (key.Length != KeySizeBytes)
            {
                throw new ArgumentException(
                    $"Secret key '{id}' is {key.Length} bytes; a 32-byte (AES-256) key is required.", nameof(keys));
            }
        }

        if (!keys.ContainsKey(activeKeyId))
        {
            throw new ArgumentException(
                $"The active secret key id '{activeKeyId}' is not among the configured keys.", nameof(activeKeyId));
        }

        _keys = keys;
        ActiveKeyId = activeKeyId;
    }

    /// <summary>
    /// Builds the keyring from configuration, failing at startup — never on the first secret
    /// write — if no valid key is present. Mirrors how the connection string and the JWT signing
    /// key are required, so a misconfiguration cannot reach production quietly.
    /// </summary>
    public static SecretKeyring FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Helios:Secrets");
        var activeKeyId = section["ActiveKeyId"];

        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in section.GetSection("Keys").GetChildren())
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
            {
                continue;
            }

            byte[] material;
            try
            {
                material = Convert.FromBase64String(entry.Value);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"Secret key '{entry.Key}' (Helios:Secrets:Keys:{entry.Key}) is not valid base64.", ex);
            }

            keys[entry.Key] = material;
        }

        if (string.IsNullOrWhiteSpace(activeKeyId) || keys.Count == 0)
        {
            throw new InvalidOperationException(
                "No secret master key configured (Helios:Secrets:ActiveKeyId + Helios:Secrets:Keys:<id>).\n" +
                "  Generate one:  [Convert]::ToBase64String((1..32 | %{ Get-Random -Max 256 }))  (or 32 random bytes)\n" +
                "  Local dev:     dotnet user-secrets set \"Helios:Secrets:ActiveKeyId\" \"k1\" --project src/Helios.Api\n" +
                "                 dotnet user-secrets set \"Helios:Secrets:Keys:k1\" \"<base64-32-bytes>\" --project src/Helios.Api\n" +
                "  Container:     set Helios__Secrets__ActiveKeyId and Helios__Secrets__Keys__k1 in the environment.");
        }

        return new SecretKeyring(activeKeyId!, keys);
    }

    /// <summary>
    /// Seals a plaintext under the active key into one envelope: nonce ‖ ciphertext ‖ tag. The
    /// nonce is fresh per call — GCM must never reuse one under the same key.
    /// </summary>
    public SealedSecret Protect(string plaintext)
    {
        var key = _keys[ActiveKeyId];
        var plain = Encoding.UTF8.GetBytes(plaintext);

        var envelope = new byte[NonceSize + plain.Length + TagSize];
        var nonce = envelope.AsSpan(0, NonceSize);
        var ciphertext = envelope.AsSpan(NonceSize, plain.Length);
        var tag = envelope.AsSpan(NonceSize + plain.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, ciphertext, tag);

        CryptographicOperations.ZeroMemory(plain);
        return new SealedSecret(ActiveKeyId, envelope);
    }

    /// <summary>Opens a sealed envelope. Throws if the wrapping key is gone or the row was tampered with.</summary>
    public string Unprotect(string keyId, byte[] envelope)
    {
        if (!_keys.TryGetValue(keyId, out var key))
        {
            throw new InvalidOperationException(
                $"Secret was sealed under key '{keyId}', which is not configured; it cannot be decrypted.");
        }

        if (envelope.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("The stored secret envelope is too short to be valid.");
        }

        var nonce = envelope.AsSpan(0, NonceSize);
        var ciphertext = envelope.AsSpan(NonceSize, envelope.Length - NonceSize - TagSize);
        var tag = envelope.AsSpan(envelope.Length - TagSize, TagSize);

        var plain = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);

        // AuthenticationTagMismatchException surfaces here if any byte of the envelope was altered.
        aes.Decrypt(nonce, ciphertext, tag, plain);

        var result = Encoding.UTF8.GetString(plain);
        CryptographicOperations.ZeroMemory(plain);
        return result;
    }
}

public readonly record struct SealedSecret(string KeyId, byte[] Envelope);
