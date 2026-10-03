using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Helios.Api.Configuration;
using Helios.Contracts.Identity;
using Helios.Contracts.Platform;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Helios.Api.Security;

/// <summary>
/// Issues the signed access token. Workspace and role are optional because a user signs in
/// before choosing a workspace, then re-authenticates against one via select-workspace.
/// </summary>
public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _options = options.Value;

    public AuthResponse Issue(
        Guid userId,
        string email,
        string? displayName,
        string securityStamp,
        Guid? workspaceId,
        WorkspaceRole? role)
    {
        var claims = IdentityClaims(userId, email, displayName, securityStamp);

        if (workspaceId is { } ws)
        {
            claims.Add(new Claim(HeliosClaims.Workspace, ws.ToString()));
        }

        if (role is { } r)
        {
            claims.Add(new Claim(HeliosClaims.Role, r.ToString()));
        }

        var (encoded, expiresAt) = Sign(claims, TimeSpan.FromMinutes(_options.TokenLifetimeMinutes));

        return new AuthResponse(
            encoded,
            expiresAt,
            new AuthenticatedUser(userId, email, displayName, workspaceId, role));
    }

    /// <summary>
    /// A short-lived platform session, issued only after an authenticator code was verified. It
    /// carries no workspace: platform staff act on the platform, not inside a customer workspace.
    /// </summary>
    public PlatformSessionResponse IssuePlatform(
        Guid userId,
        string email,
        string? displayName,
        string securityStamp,
        PlatformRole role)
    {
        var claims = IdentityClaims(userId, email, displayName, securityStamp);
        claims.Add(new Claim(HeliosClaims.PlatformRole, role.ToString()));
        claims.Add(new Claim(HeliosClaims.AuthenticationMethod, "mfa"));

        var (encoded, expiresAt) = Sign(claims, TimeSpan.FromMinutes(_options.PlatformTokenLifetimeMinutes));
        return new PlatformSessionResponse(encoded, expiresAt, role);
    }

    /// <summary>
    /// The short-lived proof that the password was right, exchanged with a second factor for a
    /// session. Issued for a separate audience, so the API's bearer validation rejects it outright.
    /// </summary>
    public MfaChallengeResponse IssueMfaChallenge(Guid userId, string securityStamp)
    {
        ArgumentException.ThrowIfNullOrEmpty(securityStamp);

        var now = clock.GetUtcNow();
        var expiresAt = now + MfaChallengeLifetime;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: MfaAudience,
            claims: [new Claim(HeliosClaims.Subject, userId.ToString()), new Claim(HeliosClaims.SecurityStamp, securityStamp)],
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new MfaChallengeResponse(true, new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>The user and stamp from a valid, unexpired challenge; null for anything else.</summary>
    public (Guid UserId, string SecurityStamp)? ReadMfaChallenge(string token)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = MfaAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                LifetimeValidator = (notBefore, expires, _, _) =>
                    expires is { } e && e > clock.GetUtcNow().UtcDateTime.AddSeconds(-30)
            }, out _);

            return Guid.TryParse(principal.FindFirstValue(HeliosClaims.Subject), out var userId) &&
                   principal.FindFirstValue(HeliosClaims.SecurityStamp) is { Length: > 0 } stamp
                ? (userId, stamp)
                : null;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    public static readonly TimeSpan MfaChallengeLifetime = TimeSpan.FromMinutes(5);

    private string MfaAudience => _options.Audience + "#mfa";

    private static List<Claim> IdentityClaims(Guid userId, string email, string? displayName, string securityStamp)
    {
        ArgumentException.ThrowIfNullOrEmpty(securityStamp);

        var claims = new List<Claim>
        {
            new(HeliosClaims.Subject, userId.ToString()),
            new(HeliosClaims.Email, email),
            new(HeliosClaims.SecurityStamp, securityStamp),
        };

        if (displayName is not null)
        {
            claims.Add(new Claim(HeliosClaims.Name, displayName));
        }

        return claims;
    }

    private (string Token, DateTimeOffset ExpiresAt) Sign(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var now = clock.GetUtcNow();
        var expiresAt = now + lifetime;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
