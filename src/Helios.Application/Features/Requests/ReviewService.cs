using System.Text;
using System.Text.Json;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Products;
using Helios.Contracts.Organizations;
using Helios.Contracts.Requests;
using Helios.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Requests;

/// <summary>An export ready to send: bytes, media type and a download name.</summary>
public sealed record ResultExport(byte[] Content, string MediaType, string FileName);

/// <summary>
/// Human review of results (plan section 4, manual operator): approve, correct or reject, with the
/// original result preserved and every decision kept with its actor; and export of a result with
/// the corrections in force. Same isolation as result reads: the caller's workspace only, keys only
/// for their own environment and scoped products. Reviewing needs <c>ReviewResults</c>; reading
/// reviews and exporting need <c>ViewResults</c>, because both reveal values.
/// </summary>
public sealed class ReviewService(
    IHeliosDbContext db,
    CallerResolver callers,
    IAuditWriter audit,
    Webhooks.WebhookOutbox outbox,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RequestReviewResponse?> SubmitAsync(Guid requestId, SubmitReviewRequest submission, CancellationToken ct)
    {
        var caller = await callers.ResolveAsync(null, [OrganizationPermission.ReviewResults], ct);
        var request = await FindAsync(caller, requestId, ct);
        if (request is null)
        {
            return null;
        }

        var result = CurrentResult(request);
        var corrections = submission.Corrections ?? new Dictionary<string, JsonElement>();

        switch (submission.Decision)
        {
            case ReviewDecisionType.Correct when corrections.Count == 0:
                throw ProductInputException.For("corrections", "A correction needs at least one corrected value.");

            case ReviewDecisionType.Approve or ReviewDecisionType.Reject when corrections.Count > 0:
                throw ProductInputException.For("corrections", "Only a 'Correct' decision carries corrections.");

            case ReviewDecisionType.Reject when string.IsNullOrWhiteSpace(submission.Reason):
                throw ProductInputException.For("reason", "Say why the result is rejected.");
        }

        if (corrections.Count > 0)
        {
            var errors = ResultCorrections.Validate(result, corrections);
            if (errors.Count > 0)
            {
                throw new ProductInputException(errors);
            }
        }

        var recorded = new ReviewDecision
        {
            ApiRequestId = request.Id,
            OrganizationId = request.OrganizationId,
            WorkspaceId = request.WorkspaceId,
            Decision = submission.Decision,
            CorrectionsJson = corrections.Count == 0 ? null : JsonSerializer.Serialize(corrections, Json),
            Reason = submission.Reason?.Trim(),
            ActorUserId = caller.UserId,
            ApiKeyId = caller.Key?.Id,
            CreatedAt = clock.GetUtcNow()
        };
        db.ReviewDecisions.Add(recorded);

        // Committed by the same save as the decision (transactional outbox).
        await outbox.EnqueueReviewAsync(request, recorded.Id, submission.Decision.ToString(),
            submission.Decision switch
            {
                ReviewDecisionType.Approve => "approved",
                ReviewDecisionType.Correct => "corrected",
                _ => "rejected"
            },
            corrections.Keys.ToList(), ct);

        // Paths, never values: the audit trail must not become a second copy of document data.
        audit.Record("request.review", nameof(ApiRequest), request.Id.ToString(),
            organizationId: request.OrganizationId,
            metadataJson: JsonSerializer.Serialize(new { decision = submission.Decision.ToString(), paths = corrections.Keys }, Json));

        await db.SaveChangesAsync(ct);

        return await BuildAsync(request, ct);
    }

    public async Task<RequestReviewResponse?> GetAsync(Guid requestId, CancellationToken ct)
    {
        var caller = await callers.ResolveAsync(null, [OrganizationPermission.ViewResults], ct);
        var request = await FindAsync(caller, requestId, ct);
        return request is null ? null : await BuildAsync(request, ct);
    }

    /// <summary>
    /// The result with corrections in force, as JSON (corrected result plus the untouched original)
    /// or CSV (one row per value: original, correction, final, page, source). Each export is audited.
    /// </summary>
    public async Task<ResultExport?> ExportAsync(Guid requestId, string format, CancellationToken ct)
    {
        if (format is not ("json" or "csv"))
        {
            throw new BadRequestException("Export format is 'json' or 'csv'.", "unsupported_format");
        }

        var caller = await callers.ResolveAsync(null, [OrganizationPermission.ViewResults], ct);
        var request = await FindAsync(caller, requestId, ct);
        if (request is null)
        {
            return null;
        }

        var original = CurrentResult(request);
        var review = await BuildAsync(request, ct);
        var name = $"{request.ProductSlug}-{request.Id:N}";

        audit.Record("request.export", nameof(ApiRequest), request.Id.ToString(),
            organizationId: request.OrganizationId,
            metadataJson: JsonSerializer.Serialize(new { format }, Json));
        await db.SaveChangesAsync(ct);

        if (format == "csv")
        {
            var csv = ResultCorrections.ToCsv(ResultCorrections.Rows(original, review.Corrections));
            return new ResultExport(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", $"{name}.csv");
        }

        var document = new
        {
            requestId = request.Id,
            product = request.ProductSlug,
            version = request.ProductVersion,
            environment = request.Environment.ToString(),
            status = request.Status.ToString(),
            completedAt = request.CompletedAt,
            review = new { state = review.State, corrections = review.Corrections },
            result = ResultCorrections.Apply(original, review.Corrections),
            original
        };

        return new ResultExport(JsonSerializer.SerializeToUtf8Bytes(document, Json), "application/json", $"{name}.json");
    }

    private async Task<ApiRequest?> FindAsync(RequestCaller caller, Guid id, CancellationToken ct)
    {
        // The workspace filter confines this to the caller's workspace; checked again explicitly.
        var request = await db.ApiRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);

        if (request is null || request.WorkspaceId != caller.WorkspaceId)
        {
            return null;
        }

        if (caller.Key is { } key &&
            (request.Environment != key.Environment || !key.ScopeList.Contains(request.ProductSlug, StringComparer.Ordinal)))
        {
            return null;
        }

        return request;
    }

    private JsonElement CurrentResult(ApiRequest request)
    {
        if (request.Status is not (ApiRequestStatus.Succeeded or ApiRequestStatus.NeedsReview))
        {
            throw new ConflictException($"This request is {request.Status}; there is no result to review.", "nothing_to_review");
        }

        if (request.ResultJson is null || request.ResultExpiresAt <= clock.GetUtcNow())
        {
            throw new GoneException("This request's result is past its retention period and is no longer available.");
        }

        using var document = JsonDocument.Parse(request.ResultJson);
        return document.RootElement.Clone();
    }

    private async Task<RequestReviewResponse> BuildAsync(ApiRequest request, CancellationToken ct)
    {
        var decisions = await db.ReviewDecisions.AsNoTracking()
            .Where(d => d.ApiRequestId == request.Id)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

        var history = decisions.Select(d => new ReviewDecisionResponse(
            d.Id, d.Decision,
            d.CorrectionsJson is null
                ? new Dictionary<string, JsonElement>()
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(d.CorrectionsJson, Json)!,
            d.Reason, d.ActorUserId, d.ApiKeyId, d.CreatedAt)).ToList();

        var inForce = new Dictionary<string, JsonElement>();
        foreach (var decision in history)
        {
            foreach (var (path, value) in decision.Corrections)
            {
                inForce[path] = value;
            }
        }

        var state = history.Count == 0
            ? request.ReviewRequired ? "pending" : "not_required"
            : history[^1].Decision switch
            {
                ReviewDecisionType.Approve => "approved",
                ReviewDecisionType.Correct => "corrected",
                _ => "rejected"
            };

        return new RequestReviewResponse(request.Id, state, inForce, history);
    }
}
