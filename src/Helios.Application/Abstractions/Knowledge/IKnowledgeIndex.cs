using Helios.Contracts.Common;

namespace Helios.Application.Abstractions.Knowledge;

public interface IKnowledgeIndex
{
    Task IngestAsync(IngestRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        Guid? projectId,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record IngestRequest
{
    public required Guid ProjectId { get; init; }
    public required string SourceKind { get; init; }
    public required string Reference { get; init; }
    public DataClassification Classification { get; init; } = DataClassification.Internal;
}

public sealed record SearchHit(
    Guid DocumentId,
    string Reference,
    string Excerpt,
    double Score);
