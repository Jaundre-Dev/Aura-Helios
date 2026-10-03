using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helios.Infrastructure.Persistence.MySql.Migrations
{
    /// <summary>
    /// The initial 12-service catalogue (HELIOS-IMPLEMENTATION-PLAN.md section 2, API-CATALOGUE.md).
    /// Seeded honestly: only <c>identity.sa-id-validate</c> has an implementation, and it is
    /// sandbox-only until billing exists. Every other product is Planned — listed, never callable.
    /// Partner products stay Planned until a contract and quality gate exist. No prices are seeded.
    /// <para>
    /// Ids are written with UUID_TO_BIN without the swap flag: big-endian, matching the driver's
    /// GuidFormat=Binary16. Part of migration 20261003100142; runs once with it.
    /// </para>
    /// </summary>
    internal static class CatalogueSeed
    {
        private const string SeededAt = "2026-10-03 00:00:00.000000";

        public static void Apply(MigrationBuilder migrationBuilder)
        {
            (string Id, string Slug, string Name, string Category, string Summary, string Delivery, string State, string Sensitivity, string Unit, string Version, string Limitations)[] products =
            [
                ("01999a3c-0001-7000-8000-000000000001", "ocr.general", "General OCR", "Documents",
                    "Text from scanned documents and photos for admin and software platforms.",
                    "AI", "Planned", "Personal", "Page", null,
                    "Planned: no OCR engine has been selected or evaluated yet."),
                ("01999a3c-0001-7000-8000-000000000002", "documents.sa-id", "SA ID document extraction", "Documents",
                    "Fields from South African identity documents for onboarding.",
                    "Hybrid", "Planned", "Personal", "Document, stated sides", null,
                    "Planned. Extraction does not establish that a document is authentic."),
                ("01999a3c-0001-7000-8000-000000000003", "documents.invoice", "Invoice extraction", "Documents",
                    "Supplier, totals, VAT and line items for accounts payable.",
                    "Hybrid", "Planned", "Personal", "Document + excess pages", null,
                    "Planned. Arithmetic warnings are checks on extracted values, not an audit."),
                ("01999a3c-0001-7000-8000-000000000004", "documents.bank-statement", "Bank statement extraction", "Documents",
                    "Structured transactions and balances for accounting, property and finance workflows.",
                    "Hybrid", "Planned", "Personal", "Document + excess pages", null,
                    "Planned. Statement arithmetic checks are not affordability, credit or fraud decisions."),
                ("01999a3c-0001-7000-8000-000000000005", "documents.payslip", "Payslip extraction", "Documents",
                    "Pay, employer and deduction capture for property and HR workflows.",
                    "Hybrid", "Planned", "Personal", "Document", null,
                    "Planned. Extraction does not verify employment or income."),
                ("01999a3c-0001-7000-8000-000000000006", "documents.proof-of-address", "Proof-of-address extraction", "Documents",
                    "Name, address, issuer and date capture for onboarding.",
                    "Hybrid", "Planned", "Personal", "Document", null,
                    "Planned. Extraction is not authoritative address verification."),
                ("01999a3c-0001-7000-8000-000000000007", "documents.classify", "Document classification", "Documents",
                    "Document type detection and routing for document workflows.",
                    "AI", "Planned", "Personal", "Document/page band", null,
                    "Planned: no classification model has been evaluated yet."),
                ("01999a3c-0001-7000-8000-000000000008", "identity.sa-id-validate", "SA ID number validation", "Identity",
                    "Format, date-of-birth and checksum validation of South African ID numbers to catch input mistakes.",
                    "Build", "Sandbox", "Personal", "Request", "1",
                    "Structural validation only. Does not confirm the number was issued, belongs to the applicant or is current; not a Home Affairs verification."),
                ("01999a3c-0001-7000-8000-000000000009", "identity.face-compare", "Face comparison", "Identity",
                    "Selfie-to-document comparison for onboarding through a specialist provider.",
                    "Partner", "Planned", "SpecialPersonal", "Comparison", null,
                    "Unavailable: requires an approved specialist provider contract, validated thresholds and POPIA biometric review."),
                ("01999a3c-0001-7000-8000-00000000000a", "identity.liveness-passive", "Passive liveness", "Identity",
                    "Specialist presentation-attack detection for onboarding.",
                    "Partner", "Planned", "SpecialPersonal", "Session with attempt allowance", null,
                    "Unavailable: requires an approved specialist provider and capture SDK. Never a generic AI judgement."),
                ("01999a3c-0001-7000-8000-00000000000b", "banking.account-verify", "Bank account verification", "Company and financial",
                    "Account and holder match to reduce payment errors.",
                    "Partner", "Planned", "Personal", "Completed check", null,
                    "Unavailable: requires an authorised verification partner contract."),
                ("01999a3c-0001-7000-8000-00000000000c", "company.lookup", "Company registration lookup", "Company and financial",
                    "Registration lookup for supplier and merchant onboarding.",
                    "Partner", "Planned", "Standard", "Lookup", null,
                    "Unavailable: requires authorised registry access with redistribution permission."),
            ];

            foreach (var p in products)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO api_products
                        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
                         current_version, limitations, created_at, created_by, updated_at, updated_by)
                    VALUES
                        (UUID_TO_BIN('{p.Id}'), '{p.Slug}', '{p.Name}', '{p.Category}', '{p.Summary}', '{p.Delivery}',
                         '{p.State}', '{p.Sensitivity}', '{p.Unit}', {(p.Version is null ? "NULL" : $"'{p.Version}'")},
                         '{p.Limitations}', '{SeededAt}', NULL, NULL, NULL);
                    """);
            }

            const string requestSchema = """
                {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"required":["idNumber"],"properties":{"idNumber":{"type":"string","maxLength":32,"description":"13-digit South African ID number. Spaces are ignored."}}}
                """;

            const string responseSchema = """
                {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","required":["valid","checks","derived","notice"],"properties":{"valid":{"type":"boolean"},"checks":{"type":"object","properties":{"format":{"enum":["pass","fail","not_evaluated"]},"dateOfBirth":{"enum":["pass","fail","not_evaluated"]},"citizenship":{"enum":["pass","fail","not_evaluated"]},"checksum":{"enum":["pass","fail","not_evaluated"]}}},"derived":{"type":"object","properties":{"dateOfBirth":{"type":["string","null"],"format":"date"},"citizenship":{"enum":["citizen","permanent_resident","refugee",null]}}},"notice":{"type":"string"}}}
                """;

            // Synthetic, widely published sample number; not a real person's record.
            migrationBuilder.Sql($$"""
                INSERT INTO api_product_versions
                    (id, product_id, version, release_state, max_input_bytes, request_schema_json, response_schema_json,
                     request_example, published_at, created_at, created_by, updated_at, updated_by)
                VALUES
                    (UUID_TO_BIN('01999a3c-0002-7000-8000-000000000008'), UUID_TO_BIN('01999a3c-0001-7000-8000-000000000008'),
                     '1', 'Sandbox', 1024, '{{requestSchema}}', '{{responseSchema}}', '{"idNumber":"8001015009087"}',
                     '{{SeededAt}}', '{{SeededAt}}', NULL, NULL, NULL);
                """);
        }
    }
}
