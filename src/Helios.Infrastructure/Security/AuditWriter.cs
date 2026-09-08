using Helios.Application.Abstractions.Security;
using Helios.Domain.Platform;
using Helios.Infrastructure.Persistence.MySql;

namespace Helios.Infrastructure.Security;

/// <summary>
/// Stages audit rows on the same change tracker as the work they describe, so a single
/// SaveChanges commits both or neither. An audit trail written in its own transaction
/// can disagree with reality when the first one rolls back; this cannot.
/// </summary>
public sealed class AuditWriter(
    HeliosDbContext db,
    IWorkspaceContext context,
    TimeProvider timeProvider) : IAuditWriter
{
    public void Record(
        string action,
        string resourceType,
        string? resourceId = null,
        bool allowed = true,
        string? denyReason = null,
        string? metadataJson = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAt = timeProvider.GetUtcNow(),
            ActorUserId = context.UserId,
            WorkspaceId = context.WorkspaceId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Allowed = allowed,
            DenyReason = denyReason,
            Metadata = metadataJson,
            IpAddress = context.IpAddress
        });
    }
}
