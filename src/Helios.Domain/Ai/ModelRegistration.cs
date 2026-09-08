using Helios.Domain.Common;

namespace Helios.Domain.Ai;

/// <summary>Model registry row (plan section 6.2).</summary>
public class ModelRegistration : AuditableEntity
{
    public Guid ProviderId { get; set; }
    public required string ModelId { get; set; }
    public required string DisplayName { get; set; }

    public int ContextWindow { get; set; }
    public int? MaxOutputTokens { get; set; }

    public bool SupportsStreaming { get; set; }
    public bool SupportsTools { get; set; }
    public bool SupportsVision { get; set; }
    public bool SupportsReasoning { get; set; }
    public bool SupportsStructuredOutput { get; set; }
    public bool SupportsEmbedding { get; set; }

    public bool IsLocal { get; set; }
    public bool IsAvailable { get; set; } = true;

    public decimal? InputCostPerMillionTokens { get; set; }
    public decimal? OutputCostPerMillionTokens { get; set; }
    public int QualityScore { get; set; }
}
