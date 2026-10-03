using Helios.Application.Abstractions.Execution;

namespace Helios.Application.Features.Execution;

/// <summary>
/// Keeps a claimed job's lease alive while its executor runs, so long work (a large document, a
/// slow provider) is not reclaimed and repeated by another worker. If a renewal finds the lease
/// already lost, <see cref="Token"/> is cancelled: the executor should stop, and nothing it produces
/// can be committed anyway, because every commit is fenced.
/// </summary>
public sealed class LeaseHeartbeat : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _loop;

    private LeaseHeartbeat(IJobLeases leases, ClaimedJob claim, ExecutionPolicy policy, TimeProvider clock, CancellationToken outer)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(outer);
        Token = _stop.Token;
        _loop = RunAsync(leases, claim, policy, clock);
    }

    /// <summary>Cancelled when the caller cancels or the lease is lost.</summary>
    public CancellationToken Token { get; }

    public bool LeaseLost { get; private set; }

    public static LeaseHeartbeat Start(IJobLeases leases, ClaimedJob claim, ExecutionPolicy policy, TimeProvider clock, CancellationToken ct) =>
        new(leases, claim, policy, clock, ct);

    private async Task RunAsync(IJobLeases leases, ClaimedJob claim, ExecutionPolicy policy, TimeProvider clock)
    {
        var interval = policy.RenewEvery > TimeSpan.Zero ? policy.RenewEvery : TimeSpan.FromSeconds(1);

        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await Task.Delay(interval, clock, _stop.Token);

                bool held;
                try
                {
                    held = await leases.RenewAsync(claim.JobId, claim.FencingToken, policy.Lease, _stop.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed renewal (database blip) is retried at the next interval; the lease
                    // still has time left, and if it lapses the fenced commit protects the outcome.
                    continue;
                }

                if (!held)
                {
                    LeaseLost = true;
                    await _stop.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped: the attempt finished, the caller cancelled, or the lease was lost.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            await _stop.CancelAsync();
        }

        await _loop;
        _stop.Dispose();
    }
}
