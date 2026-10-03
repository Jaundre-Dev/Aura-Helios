using System.Security.Cryptography;
using System.Text;
using Helios.Contracts.Catalogue;

namespace Helios.Application.Features.ApiKeys;

/// <summary>
/// Key format: <c>hk_{test|live}_{publicId}_{secret}</c>. The environment marker lets a developer
/// (and secret scanners) tell sandbox from live at a glance; the public id locates the row; the
/// 256-bit secret is what proves possession. Only SHA-256 of the whole key is stored.
/// </summary>
public static class ApiKeySecrets
{
    private const string Prefix = "hk_";
    private const int PublicIdBytes = 10;   // 80 bits → 16 base32 characters
    private const int SecretBytes = 32;     // 256 bits

    public sealed record GeneratedKey(string FullKey, string PublicId, string DisplayPrefix, byte[] Hash);

    public static GeneratedKey Generate(ApiEnvironment environment)
    {
        var publicId = Base32(RandomNumberGenerator.GetBytes(PublicIdBytes));
        var secret = Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));
        var marker = Marker(environment);

        var fullKey = $"{Prefix}{marker}_{publicId}_{secret}";

        return new GeneratedKey(fullKey, publicId, $"{Prefix}{marker}_{publicId}", Hash(fullKey));
    }

    /// <summary>Splits a presented key into environment and public id, or null when malformed.</summary>
    public static (ApiEnvironment Environment, string PublicId)? Parse(string? presented)
    {
        if (string.IsNullOrEmpty(presented) || presented.Length > 128 || !presented.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = presented[Prefix.Length..].Split('_', 3);
        if (parts.Length != 3 || parts[1].Length != 16 || parts[2].Length < 40)
        {
            return null;
        }

        ApiEnvironment? environment = parts[0] switch
        {
            "test" => ApiEnvironment.Sandbox,
            "live" => ApiEnvironment.Live,
            _ => null
        };

        return environment is { } env ? (env, parts[1]) : null;
    }

    public static bool LooksLikeKey(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static byte[] Hash(string fullKey) => SHA256.HashData(Encoding.UTF8.GetBytes(fullKey));

    public static bool Matches(string presented, byte[] storedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(presented), storedHash);

    private static string Marker(ApiEnvironment environment) =>
        environment == ApiEnvironment.Live ? "live" : "test";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Base32(byte[] bytes)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var output = new StringBuilder((bytes.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;

        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(alphabet[(buffer << (5 - bits)) & 31]);
        }

        return output.ToString();
    }
}
