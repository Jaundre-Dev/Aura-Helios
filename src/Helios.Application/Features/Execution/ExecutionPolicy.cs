namespace Helios.Application.Features.Execution;

/// <summary>Timing rules for durable execution, bound from <c>Helios:Execution</c>.</summary>
public sealed record ExecutionPolicy
{
    /// <summary>How long a claim is exclusive. A job running longer than this can be reclaimed.</summary>
    public TimeSpan Lease { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>First retry delay after a definite provider failure; doubles each attempt.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Wait before asking a provider about an ambiguous attempt.</summary>
    public TimeSpan ReconcileDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Reconciliation attempts before the job is handed to a person.</summary>
    public int MaxReconcileAttempts { get; init; } = 10;

    public TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromTicks(RetryBaseDelay.Ticks * (1L << Math.Clamp(attempt - 1, 0, 10)));
}
