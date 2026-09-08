using Helios.Contracts.Ai;

namespace Helios.Application.Abstractions.Ai;

/// <summary>
/// Plan section 6.1. One implementation per inference backend. Provider SDKs stay
/// behind this line; no application module ever references one directly.
/// </summary>
public interface IModelProvider
{
    string ProviderId { get; }

    bool IsLocal { get; }

    Task<ModelResponse> ChatAsync(
        ModelRequest request,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ModelStreamChunk> StreamAsync(
        ModelRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ModelInfo>> GetModelsAsync(
        CancellationToken cancellationToken);

    Task<EmbeddingResponse> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken);

    Task<bool> HealthCheckAsync(
        CancellationToken cancellationToken);
}
