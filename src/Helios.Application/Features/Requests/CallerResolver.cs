using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Identity;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Organizations;
using Helios.Domain.ApiKeys;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Requests;

/// <summary>Who is calling a product-facing endpoint, and in which company, workspace and environment.</summary>
public sealed record RequestCaller(
    Guid OrganizationId,
    Guid WorkspaceId,
    ApiEnvironment Environment,
    ApiKey? Key,
    Guid? UserId)
{
    public string Channel => Key is null ? "portal" : "api";
}

/// <summary>
/// One rule for every product-facing endpoint (execution, request reads, uploads): an API key acts
/// only in its own workspace and environment; a signed-in user acts in the workspace their token is
/// scoped to, with the given company permissions, in the environment they ask for (default Sandbox).
/// </summary>
public sealed class CallerResolver(IHeliosDbContext db, IWorkspaceContext context, OrganizationAccess access)
{
    public async Task<RequestCaller> ResolveAsync(
        ApiEnvironment? requestedEnvironment,
        IReadOnlyCollection<OrganizationPermission> userPermissions,
        CancellationToken ct)
    {
        if (context.ApiKeyId is { } keyId)
        {
            // Authentication already verified this key is live; read it for its authoritative scope.
            var key = await db.ApiKeys.IgnoreQueryFilters().AsNoTracking().SingleAsync(k => k.Id == keyId, ct);

            if (requestedEnvironment is { } env && env != key.Environment)
            {
                throw new ForbiddenException(
                    $"This is a {key.Environment} key and cannot act in {env}.", "key_environment_mismatch");
            }

            return new RequestCaller(key.OrganizationId, key.WorkspaceId, key.Environment, key, null);
        }

        var userId = context.UserId ?? throw new UnauthenticatedException();
        var workspaceId = context.WorkspaceId
            ?? throw new ForbiddenException("Select a workspace first.", "workspace_required");

        var organizationId = await db.Workspaces
            .Where(w => w.Id == workspaceId)
            .Select(w => w.OrganizationId)
            .SingleAsync(ct);

        await access.RequireAnyAsync(organizationId, userPermissions, ct);

        return new RequestCaller(organizationId, workspaceId, requestedEnvironment ?? ApiEnvironment.Sandbox, null, userId);
    }
}
