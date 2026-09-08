using Helios.Domain.Common;

namespace Helios.Domain.Ai;

/// <summary>A configured inference backend. Credentials live in the secret store, not here.</summary>
public class AiProvider : AuditableEntity, IAggregateRoot
{
    public required string ProviderId { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>ollama, vllm, openai, anthropic, gemini, azure-openai, ollama-cloud, generic.</summary>
    public required string Kind { get; set; }

    public string? BaseUrl { get; set; }

    /// <summary>Key into the secret store. The raw credential is never persisted here.</summary>
    public string? CredentialRef { get; set; }

    public bool IsLocal { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? LastHealthCheckAt { get; set; }
    public bool IsHealthy { get; set; }
}
