namespace Helios.Contracts.Ai;

public enum ChatRole
{
    System = 0,
    User = 1,
    Assistant = 2,
    Tool = 3
}

public sealed record ChatMessage(
    ChatRole Role,
    string Content,
    string? Name = null,
    string? ToolCallId = null,
    IReadOnlyList<ToolCall>? ToolCalls = null);
