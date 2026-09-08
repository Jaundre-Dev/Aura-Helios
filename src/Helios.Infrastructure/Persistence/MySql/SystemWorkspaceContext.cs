using Helios.Application.Abstractions.Security;

namespace Helios.Infrastructure.Persistence.MySql;

/// <summary>
/// Used by migrations, the design-time factory and the worker, where no user is acting.
/// Bypasses the workspace query filters by design — which is precisely why the API must
/// never register it.
/// </summary>
public sealed class SystemWorkspaceContext : IWorkspaceContext
{
    public Guid? UserId => null;
    public Guid? WorkspaceId => null;
    public bool IsSystem => true;
    public string? IpAddress => null;
}
