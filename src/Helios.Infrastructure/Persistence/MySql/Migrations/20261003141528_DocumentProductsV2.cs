using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helios.Infrastructure.Persistence.MySql.Migrations
{
    /// <summary>
    /// Publishes <c>documents.bank-statement</c>, <c>documents.payslip</c>, <c>documents.proof-of-address</c>
    /// and <c>documents.classify</c> v1 as <b>Sandbox</b> products. All four read native PDF text layers with
    /// deterministic rules; none is offered for live use until measured on an owner-approved representative
    /// dataset against written targets (plan P3 gate). Classification v1 is rule-based, so its delivery is
    /// recorded as Build rather than AI.
    /// </summary>
    public partial class DocumentProductsV2 : Migration
    {
        private const string PublishedAt = "2026-10-03 00:00:00.000000";

        private const string UploadRequestSchema = """
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"required":["uploadId"],"properties":{"uploadId":{"type":"string","format":"uuid","description":"Id returned by POST /api/v1/uploads. Must be a PDF in the same workspace and environment."}}}
            """;

        private const string FieldNote = "Each field is null when not found, else {value, evidence:{page, box, source}}.";

        private const string Checks = """
            "checks":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"outcome":{"enum":["pass","fail","differs","not_evaluated"]},"detail":{"type":["string","null"]}}}},"missingRequired":{"type":"array","items":{"type":"string"}},"notice":{"type":"string"}
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Publish(migrationBuilder, "documents.bank-statement", "01999a3c-0001-7000-8000-000000000004", "01999a3c-0002-7000-8000-000000000004",
                "Version 1 reads digital (native) PDF statements in English: period, opening and closing balances, account holder and dated transactions. Account numbers are masked to the last four digits. Debit or credit comes from printed markers or the running balance and is left null otherwise. Arithmetic checks are not affordability, credit or fraud decisions. Scanned statements are not read. Accuracy has not been measured on a representative dataset.",
                """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","required":["readable"],"properties":{"readable":{"type":"boolean"},"fields":{"type":"object","description":"__FIELD_NOTE__","properties":{"institution":{},"accountHolder":{},"accountNumberMasked":{},"periodFrom":{},"periodTo":{},"openingBalance":{},"closingBalance":{}}},"transactions":{"type":"array","items":{"type":"object","properties":{"date":{"type":"string","format":"date"},"description":{"type":"string"},"amount":{"type":["number","null"],"description":"Positive in, negative out; null when the direction is unknown."},"printedAmount":{"type":"number"},"direction":{"enum":["credit","debit",null]},"balance":{"type":["number","null"]},"evidence":{"type":"object"}}}},"summary":{"type":"object"},__CHECKS__}}""");

            Publish(migrationBuilder, "documents.payslip", "01999a3c-0001-7000-8000-000000000005", "01999a3c-0002-7000-8000-000000000005",
                "Version 1 reads labelled fields from digital (native) PDF payslips in English: employer, employee, employee number, pay date and period, gross, deductions, net, PAYE and UIF. Identity numbers are not extracted. It does not verify employment or income. Scanned payslips are not read. Accuracy has not been measured on a representative dataset.",
                """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","required":["readable"],"properties":{"readable":{"type":"boolean"},"fields":{"type":"object","description":"__FIELD_NOTE__","properties":{"employerName":{},"employeeName":{},"employeeNumber":{},"payDate":{},"periodFrom":{},"periodTo":{},"grossPay":{},"totalDeductions":{},"netPay":{},"paye":{},"uif":{}}},__CHECKS__}}""");

            Publish(migrationBuilder, "documents.proof-of-address", "01999a3c-0001-7000-8000-000000000006", "01999a3c-0002-7000-8000-000000000006",
                "Version 1 reads labelled name, address block, postal code, document date and issuer from digital (native) PDF documents in English. It is not authoritative address verification and does not decide regulatory acceptability; recency is reported for your own policy. Scanned documents are not read. Accuracy has not been measured on a representative dataset.",
                """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","required":["readable"],"properties":{"readable":{"type":"boolean"},"fields":{"type":"object","description":"__FIELD_NOTE__","properties":{"issuer":{},"accountHolder":{},"address":{"description":"value is an array of address lines"},"postalCode":{},"documentDate":{},"accountNumberMasked":{}}},__CHECKS__}}""");

            Publish(migrationBuilder, "documents.classify", "01999a3c-0001-7000-8000-000000000007", "01999a3c-0002-7000-8000-000000000007",
                "Version 1 is rule-based, not a trained model: it counts distinctive labels in a digital PDF text layer to choose invoice, bank_statement, payslip, proof_of_address or identity_document, and returns unknown when the evidence is unclear. Scores are counts of evidence, not probabilities. Scanned documents are not read. Accuracy has not been measured on a representative dataset.",
                """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","required":["readable"],"properties":{"readable":{"type":"boolean"},"type":{"enum":["invoice","bank_statement","payslip","proof_of_address","identity_document","unknown"]},"decision":{"enum":["matched","uncertain"]},"supportedTypes":{"type":"array","items":{"type":"string"}},"candidates":{"type":"array","items":{"type":"object","properties":{"type":{"type":"string"},"score":{"type":"integer"},"indicators":{"type":"array"}}}},"notice":{"type":"string"}}}""");

            migrationBuilder.Sql("UPDATE api_products SET delivery = 'Build' WHERE slug = 'documents.classify';");
        }

        private static void Publish(MigrationBuilder migrationBuilder, string slug, string productId, string versionId, string limitations, string responseSchema)
        {
            migrationBuilder.Sql($"""
                UPDATE api_products
                SET release_state = 'Sandbox', current_version = '1', limitations = '{limitations}'
                WHERE slug = '{slug}';
                """);

            migrationBuilder.Sql($$"""
                INSERT INTO api_product_versions
                    (id, product_id, version, release_state, max_input_bytes, request_schema_json, response_schema_json,
                     request_example, published_at, created_at, created_by, updated_at, updated_by)
                VALUES
                    (UUID_TO_BIN('{{versionId}}'), UUID_TO_BIN('{{productId}}'), '1', 'Sandbox', 256,
                     '{{UploadRequestSchema}}', '{{responseSchema.Replace("__FIELD_NOTE__", FieldNote).Replace("__CHECKS__", Checks)}}', '{"uploadId":"00000000-0000-0000-0000-000000000000"}',
                     '{{PublishedAt}}', '{{PublishedAt}}', NULL, NULL, NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM api_product_versions
                WHERE id IN (UUID_TO_BIN('01999a3c-0002-7000-8000-000000000004'), UUID_TO_BIN('01999a3c-0002-7000-8000-000000000005'),
                             UUID_TO_BIN('01999a3c-0002-7000-8000-000000000006'), UUID_TO_BIN('01999a3c-0002-7000-8000-000000000007'));
                """);

            migrationBuilder.Sql("""
                UPDATE api_products SET release_state = 'Planned', current_version = NULL
                WHERE slug IN ('documents.bank-statement', 'documents.payslip', 'documents.proof-of-address', 'documents.classify');
                """);

            migrationBuilder.Sql("UPDATE api_products SET delivery = 'AI' WHERE slug = 'documents.classify';");
        }
    }
}
