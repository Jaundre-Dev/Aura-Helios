using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Identity;
using Helios.Contracts.ApiKeys;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.Domain.ApiKeys;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.ApiKeys;

/// <summary>
/// Key lifecycle for signed-in users. Keys are created in the caller's currently selected
/// workspace, owned by that workspace's company, pinned to one environment and limited to named
/// products the company is entitled to there. The secret is returned once and never stored.
/// </summary>
public sealed class ApiKeyService(
    IHeliosDbContext db,
    IUnitOfWork unitOfWork,
    OrganizationAccess access,
    IWorkspaceContext context,
    IAuditWriter audit,
    TimeProvider clock)
{
    public const int MaxScopes = 50;

    public async Task<IReadOnlyList<ApiKeyResponse>> ListAsync(CancellationToken ct)
    {
        var (organizationId, workspaceId) = await RequireWorkspaceAsync(ct);
        await access.RequireAsync(organizationId, OrganizationPermission.ManageApiKeys, ct);

        var keys = await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.WorkspaceId == workspaceId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        return keys.Select(ToResponse).ToList();
    }

    public async Task<CreatedApiKeyResponse> CreateAsync(CreateApiKeyRequest request, CancellationToken ct)
    {
        var (organizationId, workspaceId) = await RequireWorkspaceAsync(ct);
        await access.RequireAsync(organizationId, OrganizationPermission.ManageApiKeys, ct);

        var scopes = await ValidateScopesAsync(organizationId, request.Environment, request.Scopes, ct);

        if (request.ExpiresAt is { } expiresAt && expiresAt <= clock.GetUtcNow())
        {
            throw new ConflictException("The expiry must be in the future.", "invalid_expiry");
        }

        if (request.ProjectId is { } projectId &&
            !await db.Projects.AnyAsync(p => p.Id == projectId && p.WorkspaceId == workspaceId && p.IsActive, ct))
        {
            throw new NotFoundException("Project", projectId);
        }

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await access.RequireLockedAsync(organizationId, OrganizationPermission.ManageApiKeys, token);

            var (key, secret) = NewKey(organizationId, workspaceId, request.Name.Trim(), request.Environment,
                scopes, request.ExpiresAt, request.ProjectId);

            audit.Record("api_key.create", nameof(ApiKey), key.Id.ToString(),
                organizationId: organizationId,
                metadataJson: $$"""{"prefix":"{{key.DisplayPrefix}}","environment":"{{key.Environment}}","scopes":"{{key.Scopes}}"}""");

            return new CreatedApiKeyResponse(ToResponse(key), secret);
        }, ct);
    }

    /// <summary>Revocation takes effect on the very next request: authentication reads the row every time.</summary>
    public async Task<ApiKeyResponse> RevokeAsync(Guid id, CancellationToken ct)
    {
        var (organizationId, _) = await RequireWorkspaceAsync(ct);
        await access.RequireAsync(organizationId, OrganizationPermission.ManageApiKeys, ct);

        // The workspace query filter confines this to the caller's workspace.
        var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == id, ct)
            ?? throw new NotFoundException("API key", id);

        if (key.RevokedAt is null)
        {
            key.RevokedAt = clock.GetUtcNow();
            key.RevokedBy = context.UserId;

            audit.Record("api_key.revoke", nameof(ApiKey), key.Id.ToString(),
                organizationId: organizationId,
                metadataJson: $$"""{"prefix":"{{key.DisplayPrefix}}"}""");

            await db.SaveChangesAsync(ct);
        }

        return ToResponse(key);
    }

    /// <summary>Issues a replacement with the same settings and revokes the original, atomically.</summary>
    public async Task<CreatedApiKeyResponse> RotateAsync(Guid id, CancellationToken ct)
    {
        var (organizationId, workspaceId) = await RequireWorkspaceAsync(ct);
        await access.RequireAsync(organizationId, OrganizationPermission.ManageApiKeys, ct);

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await access.RequireLockedAsync(organizationId, OrganizationPermission.ManageApiKeys, token);

            var original = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == id, token)
                ?? throw new NotFoundException("API key", id);

            if (!original.IsUsableAt(clock.GetUtcNow()))
            {
                throw new ConflictException("Only an active key can be rotated.", "key_inactive");
            }

            var (replacement, secret) = NewKey(organizationId, workspaceId, original.Name, original.Environment,
                original.ScopeList, original.ExpiresAt, original.ProjectId);

            original.RevokedAt = clock.GetUtcNow();
            original.RevokedBy = context.UserId;

            audit.Record("api_key.rotate", nameof(ApiKey), original.Id.ToString(),
                organizationId: organizationId,
                metadataJson: $$"""{"replacedBy":"{{replacement.Id}}","prefix":"{{replacement.DisplayPrefix}}"}""");

            return new CreatedApiKeyResponse(ToResponse(replacement), secret);
        }, ct);
    }

    private (ApiKey Key, string Secret) NewKey(
        Guid organizationId,
        Guid workspaceId,
        string name,
        ApiEnvironment environment,
        IReadOnlyCollection<string> scopes,
        DateTimeOffset? expiresAt,
        Guid? projectId)
    {
        var generated = ApiKeySecrets.Generate(environment);

        var key = new ApiKey
        {
            OrganizationId = organizationId,
            WorkspaceId = workspaceId,
            ProjectId = projectId,
            Name = name,
            PublicId = generated.PublicId,
            DisplayPrefix = generated.DisplayPrefix,
            SecretHash = generated.Hash,
            Environment = environment,
            Scopes = string.Join(',', scopes),
            ExpiresAt = expiresAt
        };

        db.ApiKeys.Add(key);
        return (key, generated.FullKey);
    }

    /// <summary>Each scope must be a product the company has enabled in this environment.</summary>
    private async Task<IReadOnlyList<string>> ValidateScopesAsync(
        Guid organizationId,
        ApiEnvironment environment,
        IReadOnlyList<string> requested,
        CancellationToken ct)
    {
        var scopes = requested.Select(s => s.Trim()).Distinct(StringComparer.Ordinal).ToList();

        if (scopes.Count == 0 || scopes.Count > MaxScopes)
        {
            throw new ConflictException($"A key needs between 1 and {MaxScopes} product scopes.", "invalid_scopes");
        }

        var enabled = await (
            from entitlement in db.Entitlements
            where entitlement.OrganizationId == organizationId &&
                  entitlement.Environment == environment &&
                  entitlement.State == EntitlementState.Enabled
            join product in db.ApiProducts on entitlement.ProductId equals product.Id
            where scopes.Contains(product.Slug)
            select product.Slug)
            .ToListAsync(ct);

        var missing = scopes.Except(enabled, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new ForbiddenException(
                $"These products are not enabled for {environment}: {string.Join(", ", missing)}.",
                "product_not_enabled");
        }

        return scopes;
    }

    private async Task<(Guid OrganizationId, Guid WorkspaceId)> RequireWorkspaceAsync(CancellationToken ct)
    {
        if (context.ApiKeyId is not null)
        {
            throw new ForbiddenException("API keys cannot manage API keys; sign in to the portal.", "user_required");
        }

        var workspaceId = context.WorkspaceId
            ?? throw new ForbiddenException("Select a workspace first.", "workspace_required");

        var organizationId = await db.Workspaces
            .Where(w => w.Id == workspaceId)
            .Select(w => w.OrganizationId)
            .SingleAsync(ct);

        return (organizationId, workspaceId);
    }

    internal static ApiKeyResponse ToResponse(ApiKey k) =>
        new(k.Id, k.OrganizationId, k.WorkspaceId, k.ProjectId, k.Name, k.DisplayPrefix, k.Environment,
            k.ScopeList, k.CreatedAt, k.ExpiresAt, k.RevokedAt, k.LastUsedAt);
}
