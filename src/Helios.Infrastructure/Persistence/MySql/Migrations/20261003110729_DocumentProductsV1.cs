using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helios.Infrastructure.Persistence.MySql.Migrations
{
    /// <summary>
    /// Publishes <c>ocr.general</c> and <c>documents.invoice</c> v1 as <b>Sandbox</b> products: implemented,
    /// callable in sandbox, not offered for live use until measured on an owner-approved representative
    /// dataset against written targets (plan P3 gate). Both read native PDF text layers only.
    /// </summary>
    public partial class DocumentProductsV1 : Migration
    {
        private const string PublishedAt = "2026-10-03 00:00:00.000000";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            const string uploadRequestSchema = """
                {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"required":["uploadId"],"properties":{"uploadId":{"type":"string","format":"uuid","description":"Id returned by POST /api/v1/uploads. Must be a PDF in the same workspace and environment."}}}
                """;

            const string ocrResponseSchema = """
                {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","required":["method","pageCount","pagesWithText","pages"],"properties":{"method":{"const":"pdf_text_layer"},"pageCount":{"type":"integer"},"pagesWithText":{"type":"integer"},"pages":{"type":"array","items":{"type":"object","properties":{"page":{"type":"integer"},"hasTextLayer":{"type":"boolean"},"text":{"type":"string"},"lines":{"type":"array","items":{"type":"object","properties":{"text":{"type":"string"},"box":{"type":"array","items":{"type":"number"},"minItems":4,"maxItems":4,"description":"left, top, right, bottom as fractions of the page, origin top-left"}}}}}}},"notice":{"type":"string"}}}
                """;

            const string invoiceResponseSchema = """
                {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","required":["readable"],"properties":{"readable":{"type":"boolean"},"fields":{"type":"object","description":"Each field is null when not found, else {value, evidence:{page, box, source}}.","properties":{"invoiceNumber":{},"invoiceDate":{},"dueDate":{},"supplierName":{},"supplierVatNumber":{},"customerName":{},"customerVatNumber":{},"currency":{},"subtotal":{},"vat":{},"total":{}}},"lineItems":{"type":"array"},"checks":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"outcome":{"enum":["pass","fail","differs","not_evaluated"]},"detail":{"type":["string","null"]}}}},"missingRequired":{"type":"array","items":{"type":"string"}},"notice":{"type":"string"}}}
                """;

            migrationBuilder.Sql("""
                UPDATE api_products
                SET release_state = 'Sandbox', current_version = '1',
                    limitations = 'Version 1 reads the text layer of digital (native) PDFs only. Scanned pages and images are not OCR''d: they are reported with a warning and not charged. No OCR engine has been selected, and accuracy has not been measured on a representative dataset.'
                WHERE slug = 'ocr.general';
                """);

            migrationBuilder.Sql("""
                UPDATE api_products
                SET release_state = 'Sandbox', current_version = '1',
                    limitations = 'Version 1 reads labelled fields from digital (native) PDF invoices in English; scanned invoices are not read. Unlabelled values are returned as null, never guessed. Checks cover arithmetic and formats only, not authenticity or VAT registration. Accuracy has not been measured on a representative dataset.'
                WHERE slug = 'documents.invoice';
                """);

            Version(migrationBuilder, "01999a3c-0002-7000-8000-000000000001", "01999a3c-0001-7000-8000-000000000001", uploadRequestSchema, ocrResponseSchema);
            Version(migrationBuilder, "01999a3c-0002-7000-8000-000000000003", "01999a3c-0001-7000-8000-000000000003", uploadRequestSchema, invoiceResponseSchema);
        }

        private static void Version(MigrationBuilder migrationBuilder, string id, string productId, string requestSchema, string responseSchema) =>
            migrationBuilder.Sql($$"""
                INSERT INTO api_product_versions
                    (id, product_id, version, release_state, max_input_bytes, request_schema_json, response_schema_json,
                     request_example, published_at, created_at, created_by, updated_at, updated_by)
                VALUES
                    (UUID_TO_BIN('{{id}}'), UUID_TO_BIN('{{productId}}'), '1', 'Sandbox', 256,
                     '{{requestSchema}}', '{{responseSchema}}', '{"uploadId":"00000000-0000-0000-0000-000000000000"}',
                     '{{PublishedAt}}', '{{PublishedAt}}', NULL, NULL, NULL);
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM api_product_versions
                WHERE id IN (UUID_TO_BIN('01999a3c-0002-7000-8000-000000000001'), UUID_TO_BIN('01999a3c-0002-7000-8000-000000000003'));
                """);

            migrationBuilder.Sql("""
                UPDATE api_products SET release_state = 'Planned', current_version = NULL
                WHERE slug IN ('ocr.general', 'documents.invoice');
                """);
        }
    }
}
