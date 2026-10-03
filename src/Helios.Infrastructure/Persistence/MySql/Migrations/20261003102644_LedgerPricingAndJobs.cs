using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helios.Infrastructure.Persistence.MySql.Migrations
{
    /// <inheritdoc />
    public partial class LedgerPricingAndJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "price_version_id",
                table: "api_requests",
                type: "binary(16)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reserved_amount",
                table: "api_requests",
                type: "decimal(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    api_request_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    organization_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    workspace_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    product_slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    product_version = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    phase = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    max_attempts = table.Column<int>(type: "int", nullable: false),
                    reconcile_attempts = table.Column<int>(type: "int", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    lease_owner = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    fencing_token = table.Column<long>(type: "bigint", nullable: false),
                    execution_started_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    input_envelope = table.Column<byte[]>(type: "mediumblob", nullable: true),
                    input_key_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    provider_reference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_error = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_jobs_api_requests_api_request_id",
                        column: x => x.api_request_id,
                        principalTable: "api_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    organization_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    type = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    balance = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_accounts", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ledger_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    posting_key = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    organization_id = table.Column<Guid>(type: "binary(16)", nullable: true),
                    api_request_id = table.Column<Guid>(type: "binary(16)", nullable: true),
                    payment_id = table.Column<Guid>(type: "binary(16)", nullable: true),
                    reverses_transaction_id = table.Column<Guid>(type: "binary(16)", nullable: true),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "binary(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_transactions", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "price_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    product_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    environment = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    unit = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    unit_price = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    minimum_charge = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    tax_treatment = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effective_from = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "binary(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_price_versions_api_products_product_id",
                        column: x => x.product_id,
                        principalTable: "api_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    organization_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    api_request_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    settled_amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservations", x => x.id);
                    table.ForeignKey(
                        name: "fk_reservations_api_requests_api_request_id",
                        column: x => x.api_request_id,
                        principalTable: "api_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    transaction_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    account_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "ledger_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "usage_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    organization_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    workspace_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    api_request_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    product_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    product_slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    product_version = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    environment = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    price_version_id = table.Column<Guid>(type: "binary(16)", nullable: true),
                    unit = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quantity = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_usage_events_api_requests_api_request_id",
                        column: x => x.api_request_id,
                        principalTable: "api_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usage_events_price_versions_price_version_id",
                        column: x => x.price_version_id,
                        principalTable: "price_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_api_requests_price_version_id",
                table: "api_requests",
                column: "price_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_api_request_id",
                table: "jobs",
                column: "api_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_status_available_at",
                table: "jobs",
                columns: new[] { "status", "available_at" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_status_lease_expires_at",
                table: "jobs",
                columns: new[] { "status", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_workspace_id",
                table: "jobs",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_organization_id_type_currency",
                table: "ledger_accounts",
                columns: new[] { "organization_id", "type", "currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account_id",
                table: "ledger_entries",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_transaction_id",
                table: "ledger_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_transactions_api_request_id",
                table: "ledger_transactions",
                column: "api_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_transactions_organization_id_created_at",
                table: "ledger_transactions",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_transactions_payment_id",
                table: "ledger_transactions",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_transactions_posting_key",
                table: "ledger_transactions",
                column: "posting_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_versions_product_id_environment_effective_from",
                table: "price_versions",
                columns: new[] { "product_id", "environment", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reservations_api_request_id",
                table: "reservations",
                column: "api_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reservations_organization_id_state",
                table: "reservations",
                columns: new[] { "organization_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_api_request_id",
                table: "usage_events",
                column: "api_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_organization_id_occurred_at",
                table: "usage_events",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_price_version_id",
                table: "usage_events",
                column: "price_version_id");

            migrationBuilder.AddForeignKey(
                name: "fk_api_requests_price_versions_price_version_id",
                table: "api_requests",
                column: "price_version_id",
                principalTable: "price_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_api_requests_price_versions_price_version_id",
                table: "api_requests");

            migrationBuilder.DropTable(
                name: "jobs");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "reservations");

            migrationBuilder.DropTable(
                name: "usage_events");

            migrationBuilder.DropTable(
                name: "ledger_accounts");

            migrationBuilder.DropTable(
                name: "ledger_transactions");

            migrationBuilder.DropTable(
                name: "price_versions");

            migrationBuilder.DropIndex(
                name: "ix_api_requests_price_version_id",
                table: "api_requests");

            migrationBuilder.DropColumn(
                name: "price_version_id",
                table: "api_requests");

            migrationBuilder.DropColumn(
                name: "reserved_amount",
                table: "api_requests");
        }
    }
}
