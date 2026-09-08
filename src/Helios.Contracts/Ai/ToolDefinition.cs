namespace Helios.Contracts.Ai;

/// <summary>Provider-neutral tool schema handed to a model.</summary>
public sealed record ToolDefinition(
    string Name,
    string Description,
    string JsonSchema);

public sealed record ToolCall(
    string Id,
    string Name,
    string ArgumentsJson);
