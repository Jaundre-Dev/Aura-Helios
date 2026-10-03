using Helios.Domain.Common;

namespace Helios.Domain.Execution;

public enum JobStatus
{
    /// <summary>Waiting for <see cref="Job.AvailableAt"/>, then claimable.</summary>
    Queued,

    /// <summary>Leased to one worker until <see cref="Job.LeaseExpiresAt"/>.</summary>
    Running,

    /// <summary>A provider call may or may not have completed; waiting to ask the provider.</summary>
    Reconciling,

    Succeeded,
    Failed,
    Cancelled,

    /// <summary>Reconciliation could not decide; a person must resolve it. Reservation stays held.</summary>
    NeedsReview
}

public enum JobPhase
{
    Execute,
    Reconcile
}

/// <summary>
/// The durable execution record behind an <c>ApiRequest</c>, kept in MySQL so accepted work and
/// its money survive any restart. A worker owns a job only while its lease is valid, and every
/// state change is conditioned on the <see cref="FencingToken"/> it claimed with: a worker whose
/// lease expired and was reclaimed can no longer commit anything.
/// </summary>
public class Job : Entity, IAggregateRoot
{
    public Guid ApiRequestId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string ProductSlug { get; set; }
    public required string ProductVersion { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Queued;
    public JobPhase Phase { get; set; } = JobPhase.Execute;

    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public int ReconcileAttempts { get; set; }

    public DateTimeOffset AvailableAt { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public long FencingToken { get; set; }

    /// <summary>Set, and committed, immediately before an executor is called in this attempt.</summary>
    public DateTimeOffset? ExecutionStartedAt { get; set; }

    /// <summary>The original request body, sealed with the secret keyring. Purged at a terminal state.</summary>
    public byte[]? InputEnvelope { get; set; }
    public string? InputKeyId { get; set; }

    public string? ProviderReference { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public bool IsTerminal => Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled;
}
