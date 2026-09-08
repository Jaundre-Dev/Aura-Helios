using Helios.Contracts.Ai;

namespace Helios.Application.Abstractions.Ai;

public interface IModelRegistry
{
    Task<IReadOnlyList<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken);

    Task<ModelInfo?> FindAsync(string providerId, string modelId, CancellationToken cancellationToken);

    Task RefreshAsync(CancellationToken cancellationToken);
}
