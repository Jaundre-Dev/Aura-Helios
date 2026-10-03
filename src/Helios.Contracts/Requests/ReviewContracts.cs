using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helios.Contracts.Requests;

[JsonConverter(typeof(JsonStringEnumConverter<ReviewDecisionType>))]
public enum ReviewDecisionType
{
    /// <summary>The result (with any earlier corrections) is accepted as it stands.</summary>
    Approve,

    /// <summary>One or more values are corrected; the original result is kept unchanged.</summary>
    Correct,

    /// <summary>The result is not usable (wrong document, unreadable, not what was expected).</summary>
    Reject
}

/// <param name="Corrections">
/// For <see cref="ReviewDecisionType.Correct"/>: new values keyed by path — <c>fields.total</c>, or
/// <c>transactions[2].direction</c> for an array item's property. Paths must exist in the result.
/// </param>
public sealed record SubmitReviewRequest(
    ReviewDecisionType Decision,
    IReadOnlyDictionary<string, JsonElement>? Corrections,
    string? Reason);

public sealed record ReviewDecisionResponse(
    Guid Id,
    ReviewDecisionType Decision,
    IReadOnlyDictionary<string, JsonElement> Corrections,
    string? Reason,
    Guid? ActorUserId,
    Guid? ApiKeyId,
    DateTimeOffset CreatedAt);

/// <param name="State"><c>not_required</c>, <c>pending</c>, <c>approved</c>, <c>corrected</c> or <c>rejected</c>.</param>
/// <param name="Corrections">Every correction in force, later decisions overriding earlier ones.</param>
public sealed record RequestReviewResponse(
    Guid RequestId,
    string State,
    IReadOnlyDictionary<string, JsonElement> Corrections,
    IReadOnlyList<ReviewDecisionResponse> History);
