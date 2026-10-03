using System.Text.Json;
using Helios.Application.Abstractions.Documents;

namespace Helios.Application.Features.Products.Documents;

/// <summary>Input shared by document products: <c>{"uploadId": "…"}</c> and nothing else.</summary>
internal static class DocumentInput
{
    public const string Pdf = "application/pdf";

    public static ParsedProductInput Parse(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw ProductInputException.For("body", "Expected a JSON object.");
        }

        if (!body.TryGetProperty("uploadId", out var value) || value.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(value.GetString(), out var uploadId))
        {
            throw ProductInputException.For("uploadId", "uploadId is required: the id returned by POST /api/v1/uploads.");
        }

        foreach (var property in body.EnumerateObject())
        {
            if (property.Name != "uploadId")
            {
                throw ProductInputException.For(property.Name, $"Unknown field '{property.Name}'.");
            }
        }

        // Canonical form is the upload id: the same document processed twice under one
        // Idempotency-Key is the same request.
        return new ParsedProductInput(uploadId, uploadId.ToString("N"));
    }

    public static Guid UploadId(ParsedProductInput input) => (Guid)input.Value;

    /// <summary>Reads the text layer, failing the request (not retrying) if the document is gone.</summary>
    public static async Task<PdfText> ReadAsync(
        ParsedProductInput input, ProductExecutionContext context, IPdfTextReader reader, CancellationToken ct)
    {
        var uploads = context.Uploads ?? throw new InvalidOperationException("Document products need upload access.");

        var bytes = await uploads.OpenAsync(UploadId(input), ct)
            ?? throw new ProductExecutionFailedException("upload_unavailable",
                "The upload was deleted or expired before it could be processed.");

        return reader.Read(bytes);
    }

    public static string NoTextWarning(int page) =>
        $"Page {page} has no text layer (likely a scan). This version does not OCR images; the page was not read and not charged.";
}
