using Microsoft.Extensions.DependencyInjection;

namespace Helios.Application.Abstractions.Execution;

/// <summary>
/// Service scopes for background work. A job is processed in a scope whose workspace context is
/// that job's own workspace — never in an unrestricted system scope — so every query filter still
/// confines it to its tenant. Only the claim, which hands out ids, runs with system scope.
/// </summary>
public interface ITenantScopeFactory
{
    IServiceScope CreateForWorkspace(Guid workspaceId);

    IServiceScope CreateSystem();
}
