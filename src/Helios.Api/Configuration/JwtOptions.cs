using System.ComponentModel.DataAnnotations;

namespace Helios.Api.Configuration;

/// <summary>
/// JWT signing and validation parameters, bound from <c>Helios:Jwt</c> and validated at
/// startup. This is the WP0.1 rule applied to auth: a bad or missing signing key must stop
/// the process, not surface on the first sign-in. The key is never in appsettings.json —
/// set it in user-secrets or the environment, exactly as the connection string is.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Helios:Jwt";

    [Required]
    public string Issuer { get; init; } = "helios";

    [Required]
    public string Audience { get; init; } = "helios-web";

    /// <summary>
    /// HMAC-SHA256 signing key. At least 32 bytes: a shorter key is smaller than the
    /// HS256 output and the handler rejects it, so we fail here with a clearer message.
    /// </summary>
    [Required]
    [MinLength(32, ErrorMessage = "Helios:Jwt:SigningKey must be at least 32 characters.")]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 1440)]
    public int TokenLifetimeMinutes { get; init; } = 60;
}
