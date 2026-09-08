using Helios.Contracts.Ai;

namespace Helios.Application.Abstractions.Ai;

/// <summary>
/// The single entry point every module uses to reach a model. Applies routing,
/// budgets, data-classification policy, telemetry and fallback.
/// </summary>
public interface IModelGateway
{
    Task<ModelResponse> ChatAsync(
        ModelRequest request,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ModelStreamChunk> StreamAsync(
        ModelRequest request,
        CancellationToken cancellationToken);

    Task<EmbeddingResponse> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken);
}
