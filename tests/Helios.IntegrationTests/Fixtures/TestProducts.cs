using System.Collections.Concurrent;
using System.Text.Json;
using Helios.Application.Abstractions.Execution;
using Helios.Application.Features.Products;
using Microsoft.Extensions.DependencyInjection;

namespace Helios.IntegrationTests.Fixtures;

/// <summary>
/// Test-only products, registered only by the integration test host and named <c>test.*</c> so they
/// can never be mistaken for real services. They exist because no real product is billable yet,
/// and the ledger, jobs and reconciliation must be proven before one is.
/// </summary>
public static class TestProducts
{
    public const string Metered = "test.metered";
    public const string Provider = "test.provider";

    public const decimal MeteredUnitPrice = 1.00m;
    public const decimal ProviderUnitPrice = 2.50m;

    internal static ParsedProductInput ParseObject(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw ProductInputException.For("body", "Expected a JSON object.");
        }

        return new ParsedProductInput(body.Clone(), body.GetRawText());
    }

    internal static decimal Units(ParsedProductInput input, string name, decimal fallback) =>
        ((JsonElement)input.Value).TryGetProperty(name, out var value) ? value.GetDecimal() : fallback;

    internal static string Text(ParsedProductInput input, string name, string fallback) =>
        ((JsonElement)input.Value).TryGetProperty(name, out var value) ? value.GetString()! : fallback;

    internal static ProductOutcome Outcome(decimal units) =>
        new(JsonSerializer.SerializeToElement(new { ok = true, units }), [], false, "unit", units);
}

/// <summary>
/// Synchronous, deterministic, safe to repeat. Input: <c>units</c> (the estimate),
/// <c>actualUnits</c> (what is consumed, default = units), <c>mode</c> = <c>ok</c> | <c>fail</c>.
/// </summary>
public sealed class TestMeteredExecutor : IProductExecutor
{
    public string ProductSlug => TestProducts.Metered;
    public string Version => "1";
    public ExecutionMode Mode => ExecutionMode.Synchronous;
    public bool SafeToRepeat => true;

    public ParsedProductInput Parse(JsonElement body) => TestProducts.ParseObject(body);

    public decimal EstimateMaxUnits(ParsedProductInput input) => TestProducts.Units(input, "units", 1m);

    public Task<ProductOutcome> ExecuteAsync(ParsedProductInput input, ProductExecutionContext context, CancellationToken ct)
    {
        if (TestProducts.Text(input, "mode", "ok") == "fail")
        {
            throw new ProviderUnavailableException("Simulated platform failure.");
        }

        var units = TestProducts.Units(input, "actualUnits", TestProducts.Units(input, "units", 1m));
        return Task.FromResult(TestProducts.Outcome(units));
    }

    public Task<ReconcileOutcome> ReconcileAsync(string? reference, ProductExecutionContext context, CancellationToken ct) =>
        Task.FromResult(ReconcileOutcome.NotCompleted);
}

/// <summary>
/// Shared record of what the simulated provider actually did, so tests can prove a provider call
/// was never blindly repeated.
/// </summary>
public sealed class TestProviderLog
{
    public ConcurrentDictionary<Guid, int> Calls { get; } = new();
    public ConcurrentDictionary<Guid, bool> CompletedAtProvider { get; } = new();
    public ConcurrentDictionary<Guid, string> Scenarios { get; } = new();

    public int CallsFor(Guid requestId) => Calls.GetValueOrDefault(requestId);

    /// <summary>Completed when a <c>slow</c> attempt has started calling the "provider".</summary>
    public TaskCompletionSource Started(Guid requestId) =>
        _started.GetOrAdd(requestId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    /// <summary>A <c>slow</c> attempt finishes when this is completed (or its token is cancelled).</summary>
    public TaskCompletionSource Gate(Guid requestId) =>
        _gates.GetOrAdd(requestId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public ConcurrentDictionary<Guid, bool> Cancelled { get; } = new();

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _started = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _gates = new();
}

/// <summary>
/// Asynchronous and NOT safe to repeat, like a paid external check. Input <c>scenario</c>:
/// <list type="bullet">
///   <item><c>ok</c> — completes.</item>
///   <item><c>unknown</c> — provider does the work but the response is lost; reconcile finds it done.</item>
///   <item><c>unknown-not-done</c> — response lost and work not done; reconcile says so; a re-run completes.</item>
///   <item><c>unknown-forever</c> — the provider never knows.</item>
///   <item><c>crash</c> — provider does the work, then this process "dies" mid-call.</item>
///   <item><c>unavailable</c> — the provider refuses every attempt.</item>
/// </list>
/// </summary>
public sealed class TestProviderExecutor(TestProviderLog log) : IProductExecutor
{
    public string ProductSlug => TestProducts.Provider;
    public string Version => "1";
    public ExecutionMode Mode => ExecutionMode.Asynchronous;
    public bool SafeToRepeat => false;

    public ParsedProductInput Parse(JsonElement body) => TestProducts.ParseObject(body);

    public decimal EstimateMaxUnits(ParsedProductInput input) => TestProducts.Units(input, "units", 1m);

    public Task<ProductOutcome> ExecuteAsync(ParsedProductInput input, ProductExecutionContext context, CancellationToken ct)
    {
        var call = log.Calls.AddOrUpdate(context.RequestId, 1, (_, n) => n + 1);
        var units = TestProducts.Units(input, "units", 1m);
        var reference = $"prov-{context.RequestId:N}";
        var scenario = TestProducts.Text(input, "scenario", "ok");
        log.Scenarios[context.RequestId] = scenario;

        switch (scenario)
        {
            case "unknown":
                log.CompletedAtProvider[context.RequestId] = true;
                throw new ProviderOutcomeUnknownException("Simulated timeout after send.", reference);

            case "unknown-not-done" when call == 1:
            case "unknown-forever":
                throw new ProviderOutcomeUnknownException("Simulated timeout after send.", reference);

            case "crash" when call == 1:
                log.CompletedAtProvider[context.RequestId] = true;
                throw new OperationCanceledException("Simulated process death during the provider call.");

            case "unavailable":
                throw new ProviderUnavailableException("Simulated provider refusal.");

            case "slow":
                return SlowAsync(context.RequestId, units, ct);
        }

        log.CompletedAtProvider[context.RequestId] = true;
        return Task.FromResult(TestProducts.Outcome(units));
    }

    /// <summary>A long provider call: runs until the test opens the gate, or stops when cancelled.</summary>
    private async Task<ProductOutcome> SlowAsync(Guid requestId, decimal units, CancellationToken ct)
    {
        log.Started(requestId).TrySetResult();
        try
        {
            await log.Gate(requestId).Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            log.Cancelled[requestId] = true;
            throw;
        }

        log.CompletedAtProvider[requestId] = true;
        return TestProducts.Outcome(units);
    }

    public Task<ReconcileOutcome> ReconcileAsync(string? reference, ProductExecutionContext context, CancellationToken ct)
    {
        if (log.CompletedAtProvider.GetValueOrDefault(context.RequestId))
        {
            return Task.FromResult(ReconcileOutcome.Completed(TestProducts.Outcome(1m)));
        }

        return Task.FromResult(log.Scenarios.GetValueOrDefault(context.RequestId) == "unknown-forever"
            ? ReconcileOutcome.StillUnknown
            : ReconcileOutcome.NotCompleted);
    }
}

/// <summary>Tenant scopes for the in-process worker, using the factory's per-scope identity override.</summary>
public sealed class TestTenantScopes(IServiceScopeFactory scopes) : ITenantScopeFactory
{
    public IServiceScope CreateForWorkspace(Guid workspaceId) =>
        Create(new TestWorkspaceContext { WorkspaceId = workspaceId });

    public IServiceScope CreateSystem() => Create(new TestWorkspaceContext { IsSystem = true });

    private IServiceScope Create(TestWorkspaceContext identity)
    {
        var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestScopeIdentity>().Override = identity;
        return scope;
    }
}
