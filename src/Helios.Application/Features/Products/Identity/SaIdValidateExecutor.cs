using System.Text.Json;
using Helios.Contracts.Catalogue;

namespace Helios.Application.Features.Products.Identity;

/// <summary>
/// <c>identity.sa-id-validate</c> v1: structural validation of a South African ID number. Fully
/// deterministic first-party code — the same in sandbox and live, with no provider and no
/// synthetic data. The response says plainly that this is not identity verification.
/// </summary>
public sealed class SaIdValidateExecutor(TimeProvider clock) : IProductExecutor
{
    public const string Slug = "identity.sa-id-validate";
    public const string CurrentVersion = "1";
    public const int MaxInputBytes = 1024;

    public const string Notice =
        "Format, date and checksum validation only. This does not confirm that the number was issued, " +
        "belongs to the person presenting it, or is current, and it is not a Department of Home Affairs verification. " +
        "The century of birth is inferred from a two-digit year.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ProductSlug => Slug;
    public string Version => CurrentVersion;

    public ParsedProductInput Parse(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw ProductInputException.For("body", "Expected a JSON object.");
        }

        if (!body.TryGetProperty("idNumber", out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw ProductInputException.For("idNumber", "idNumber is required and must be a string.");
        }

        var idNumber = value.GetString()!;

        // Bounded so an oversized string is rejected as input, not processed. Spaces are allowed
        // as separators, so the bound is generous relative to thirteen digits.
        if (idNumber.Length > 32)
        {
            throw ProductInputException.For("idNumber", "idNumber must be at most 32 characters.");
        }

        foreach (var property in body.EnumerateObject())
        {
            if (property.Name != "idNumber")
            {
                throw ProductInputException.For(property.Name, $"Unknown field '{property.Name}'.");
            }
        }

        // Canonical form ignores separator spaces, so "800101 5009 087" and "8001015009087"
        // are the same request for idempotency.
        var canonical = idNumber.Replace(" ", string.Empty, StringComparison.Ordinal);

        return new ParsedProductInput(idNumber, canonical);
    }

    public Task<ProductOutcome> ExecuteAsync(
        ParsedProductInput input,
        ApiEnvironment environment,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var outcome = SaIdNumber.Validate((string)input.Value, today);

        var result = new
        {
            valid = outcome.Valid,
            checks = new
            {
                format = Name(outcome.Checks.Format),
                dateOfBirth = Name(outcome.Checks.DateOfBirth),
                citizenship = Name(outcome.Checks.Citizenship),
                checksum = Name(outcome.Checks.Checksum),
            },
            derived = new
            {
                dateOfBirth = outcome.DateOfBirth?.ToString("yyyy-MM-dd"),
                citizenship = outcome.Citizenship,
            },
            notice = Notice,
        };

        return Task.FromResult(new ProductOutcome(
            JsonSerializer.SerializeToElement(result, Json),
            [],
            ReviewRequired: false,
            UsageUnit: "request",
            UsageQuantity: 1m));
    }

    private static string Name(CheckOutcome outcome) => outcome switch
    {
        CheckOutcome.Pass => "pass",
        CheckOutcome.Fail => "fail",
        _ => "not_evaluated"
    };
}
