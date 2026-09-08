namespace Helios.Contracts.Common;

/// <summary>
/// Modules request a capability, never a vendor. The router maps TaskType to a model.
/// </summary>
public enum TaskType
{
    General = 0,
    Planning,
    CodeGeneration,
    CodeReview,
    Summarization,
    Classification,
    Extraction,
    Reasoning,
    Embedding,
    ToolUse,
    Vision
}
