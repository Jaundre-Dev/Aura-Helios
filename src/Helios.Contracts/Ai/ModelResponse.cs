namespace Helios.Contracts.Ai;

public sealed record ModelResponse
{
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public required string Content { get; init; }

    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = [];
    public string? FinishReason { get; init; }
    public ModelUsage Usage { get; init; } = new();
    public TimeSpan Latency { get; init; }

    /// <summary>Set when the router fell back off the preferred model.</summary>
    public string? FallbackReason { get; init; }
}

public sealed record ModelUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens => PromptTokens + CompletionTokens;
    public decimal? EstimatedCost { get; init; }
}
