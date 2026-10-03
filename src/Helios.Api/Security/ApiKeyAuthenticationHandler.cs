using System.Security.Claims;
using System.Text.Encodings.Web;
using Helios.Application.Features.ApiKeys;
using Helios.Infrastructure.Persistence.MySql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helios.Api.Security;

/// <summary>
/// Authenticates <c>hk_…</c> API keys from <c>X-Api-Key</c> or <c>Authorization: Bearer</c>. Every
/// request reads the key row, so revocation, expiry and company/workspace deactivation take effect
/// immediately — there is no cache to wait out. The principal carries no user: API keys reach
/// product execution and request reads only, never portal management.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    HeliosDbContext db,
    TimeProvider clock,
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    /// <summary>How stale last-used may get before it is rewritten; avoids a write on every call.</summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    public static string? ReadPresentedKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue(HeaderName, out var header) && header.Count == 1)
        {
            return header[0];
        }

        var authorization = request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";

        return authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase) &&
               ApiKeySecrets.LooksLikeKey(authorization[bearer.Length..].Trim())
            ? authorization[bearer.Length..].Trim()
            : null;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ReadPresentedKey(Request);
        if (presented is null)
        {
            return AuthenticateResult.NoResult();
        }

        // One message for every failure, so the response does not reveal which part was wrong.
        var invalid = AuthenticateResult.Fail("Invalid API key.");

        if (ApiKeySecrets.Parse(presented) is not { } parsed)
        {
            return invalid;
        }

        var key = await db.ApiKeys
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(k => k.PublicId == parsed.PublicId, Context.RequestAborted);

        var now = clock.GetUtcNow();

        if (key is null ||
            key.Environment != parsed.Environment ||
            !ApiKeySecrets.Matches(presented, key.SecretHash) ||
            !key.IsUsableAt(now))
        {
            return invalid;
        }

        var tenantLive = await (
            from workspace in db.Workspaces.IgnoreQueryFilters()
            where workspace.Id == key.WorkspaceId && workspace.IsActive && workspace.OrganizationId == key.OrganizationId
            join organization in db.Organizations on workspace.OrganizationId equals organization.Id
            where organization.IsActive
            select workspace.Id).AnyAsync(Context.RequestAborted);

        if (!tenantLive)
        {
            return invalid;
        }

        if (key.LastUsedAt is null || now - key.LastUsedAt > LastUsedResolution)
        {
            await db.ApiKeys.IgnoreQueryFilters()
                .Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), Context.RequestAborted);
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(HeliosClaims.ApiKey, key.Id.ToString()),
            new Claim(HeliosClaims.Workspace, key.WorkspaceId.ToString()),
            new Claim(HeliosClaims.Organization, key.OrganizationId.ToString()),
            new Claim(HeliosClaims.Environment, key.Environment.ToString()),
        ], SchemeName, HeliosClaims.ApiKey, HeliosClaims.Role);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
