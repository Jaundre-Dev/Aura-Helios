namespace Helios.Contracts.Ai;

public sealed record ModelStreamChunk
{
    public string? ContentDelta { get; init; }
    public ToolCall? ToolCall { get; init; }
    public bool IsFinal { get; init; }
    public string? FinishReason { get; init; }
    public ModelUsage? Usage { get; init; }
}
