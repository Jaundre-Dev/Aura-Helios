namespace Helios.Application.Abstractions.Security;

/// <summary>
/// Records what was done and by whom. Plan section 28.1: every important action is
/// auditable.
/// </summary>
/// <remarks>
/// <see cref="Record"/> stages the row on the same change tracker as the work it
/// describes, so one <c>SaveChangesAsync</c> commits both. An audit trail written in a
/// second transaction can disagree with reality when the first one rolls back — this
/// cannot.
/// </remarks>
public interface IAuditWriter
{
    void Record(
        string action,
        string resourceType,
        string? resourceId = null,
        bool allowed = true,
        string? denyReason = null,
        string? metadataJson = null);
}
