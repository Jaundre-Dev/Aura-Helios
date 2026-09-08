using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Helios.Api.Configuration;
using Helios.Api.Security;
using Helios.Contracts.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Helios.IntegrationTests;

/// <summary>
/// The token issuer in isolation (no host, no database): the token it writes must validate
/// against the same parameters the API validates with, and carry the plan's claims —
/// <c>sub</c>, <c>workspace_id</c>, <c>role</c> — verbatim.
/// </summary>
public sealed class JwtTokenIssuerTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "helios-test",
        Audience = "helios-test",
        SigningKey = "unit-test-signing-key-that-is-32-bytes-plus",
        TokenLifetimeMinutes = 30
    };

    private static JwtTokenIssuer Issuer() =>
        new(Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);

    [Fact]
    public void A_scoped_token_validates_and_carries_the_expected_claims()
    {
        var userId = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();

        var auth = Issuer().Issue(userId, "ada@helios.test", "Ada", workspaceId, WorkspaceRole.Admin);

        var principal = Validate(auth.AccessToken);

        Assert.Equal(userId, principal.UserId());
        Assert.Equal(workspaceId, principal.WorkspaceId());
        Assert.Equal(WorkspaceRole.Admin, principal.Role());
        Assert.Equal("ada@helios.test", principal.Email());
    }

    [Fact]
    public void A_workspaceless_token_omits_the_workspace_and_role_claims()
    {
        var auth = Issuer().Issue(Guid.CreateVersion7(), "no-ws@helios.test", null, null, null);

        var principal = Validate(auth.AccessToken);

        Assert.Null(principal.WorkspaceId());
        Assert.Null(principal.Role());
    }

    [Fact]
    public void A_token_signed_with_another_key_is_rejected()
    {
        var auth = Issuer().Issue(Guid.CreateVersion7(), "ada@helios.test", null, null, null);

        var wrongKey = new TokenValidationParameters
        {
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("a-completely-different-signing-key-32bytes")),
            ValidateIssuerSigningKey = true
        };

        var handler = new JwtSecurityTokenHandler();

        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(auth.AccessToken, wrongKey, out _));
    }

    private static System.Security.Claims.ClaimsPrincipal Validate(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();   // keep sub/role/workspace_id verbatim

        return handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Options.Issuer,
            ValidateAudience = true,
            ValidAudience = Options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Options.SigningKey)),
            ValidateLifetime = true,
            NameClaimType = HeliosClaims.Subject,
            RoleClaimType = HeliosClaims.Role
        }, out _);
    }
}
