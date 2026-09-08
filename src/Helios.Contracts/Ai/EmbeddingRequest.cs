using Helios.Contracts.Common;

namespace Helios.Contracts.Ai;

public sealed record EmbeddingRequest
{
    public required IReadOnlyList<string> Inputs { get; init; }
    public string? ProviderId { get; init; }
    public string? ModelId { get; init; }
    public DataClassification Classification { get; init; } = DataClassification.Internal;
}

public sealed record EmbeddingResponse
{
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public required IReadOnlyList<float[]> Vectors { get; init; }
    public int Dimensions { get; init; }
    public ModelUsage Usage { get; init; } = new();
}
