using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Helios.Application.Features.Platform;

/// <summary>
/// RFC 6238 time-based one-time passwords (HMAC-SHA1, 6 digits, 30-second steps) — the format
/// every common authenticator app reads from an <c>otpauth://</c> URI. Verification returns the
/// matched time step so the caller can refuse any step at or before the last one accepted.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;

    /// <summary>Steps either side of now that are accepted, to tolerate clock drift.</summary>
    public const int Window = 1;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Code(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];

        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The time step a code matches within the window around <paramref name="now"/>, or null.
    /// Compared in constant time, and every candidate step is checked, so timing does not reveal
    /// which step matched.
    /// </summary>
    public static long? Verify(byte[] secret, string code, DateTimeOffset now)
    {
        var normalised = code.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalised.Length != Digits || !normalised.All(char.IsAsciiDigit))
        {
            return null;
        }

        var presented = Encoding.ASCII.GetBytes(normalised);
        var current = StepAt(now);
        long? matched = null;

        for (var step = current - Window; step <= current + Window; step++)
        {
            if (CryptographicOperations.FixedTimeEquals(presented, Encoding.ASCII.GetBytes(Code(secret, step))))
            {
                matched ??= step;
            }
        }

        return matched;
    }

    public static string OtpAuthUri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={Base32(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";

    /// <summary>RFC 4648 base32 without padding, as authenticator apps expect.</summary>
    public static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;

        foreach (var b in data)
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
