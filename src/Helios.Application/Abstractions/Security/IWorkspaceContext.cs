namespace Helios.Application.Abstractions.Security;

/// <summary>
/// Who is acting and where. Resolved from claims per request, and consumed by the
/// global query filters and the audit trail so isolation is the default rather than
/// something each query has to remember.
/// </summary>
public interface IWorkspaceContext
{
    Guid? UserId { get; }

    Guid? WorkspaceId { get; }

    /// <summary>True for the worker and for migrations, where no user is acting.</summary>
    bool IsSystem { get; }

    /// <summary>
    /// Caller address for the audit trail, or null when the action did not originate
    /// from a request. Lives here so Infrastructure never needs a reference to
    /// ASP.NET Core just to record who called.
    /// </summary>
    string? IpAddress { get; }
}
