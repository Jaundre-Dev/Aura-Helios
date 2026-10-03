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
