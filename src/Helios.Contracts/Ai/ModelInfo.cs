namespace Helios.Contracts.Ai;

/// <summary>One row of the model registry (plan section 6.2).</summary>
public sealed record ModelInfo
{
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public required string DisplayName { get; init; }

    public int ContextWindow { get; init; }
    public int? MaxOutputTokens { get; init; }

    public bool SupportsStreaming { get; init; }
    public bool SupportsTools { get; init; }
    public bool SupportsVision { get; init; }
    public bool SupportsReasoning { get; init; }
    public bool SupportsStructuredOutput { get; init; }
    public bool SupportsEmbedding { get; init; }

    public bool IsLocal { get; init; }
    public bool IsAvailable { get; init; } = true;

    public decimal? InputCostPerMillionTokens { get; init; }
    public decimal? OutputCostPerMillionTokens { get; init; }

    public DateTimeOffset? LastHealthCheckAt { get; init; }
}
