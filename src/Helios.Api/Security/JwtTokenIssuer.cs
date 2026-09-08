using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Helios.Api.Configuration;
using Helios.Contracts.Identity;
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
        Guid? workspaceId,
        WorkspaceRole? role)
    {
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.TokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(HeliosClaims.Subject, userId.ToString()),
            new(HeliosClaims.Email, email),
        };

        if (displayName is not null)
        {
            claims.Add(new Claim(HeliosClaims.Name, displayName));
        }

        if (workspaceId is { } ws)
        {
            claims.Add(new Claim(HeliosClaims.Workspace, ws.ToString()));
        }

        if (role is { } r)
        {
            claims.Add(new Claim(HeliosClaims.Role, r.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);

        return new AuthResponse(
            encoded,
            expiresAt,
            new AuthenticatedUser(userId, email, displayName, workspaceId, role));
    }
}
