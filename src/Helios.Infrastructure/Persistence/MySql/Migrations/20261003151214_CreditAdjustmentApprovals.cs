using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helios.Infrastructure.Persistence.MySql.Migrations
{
    /// <inheritdoc />
    public partial class CreditAdjustmentApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credit_adjustment_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    organization_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requested_by = table.Column<Guid>(type: "binary(16)", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    state = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    decided_by = table.Column<Guid>(type: "binary(16)", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    decision_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_adjustment_approvals", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_adjustment_approvals_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_credit_adjustment_approvals_organization_id_reference",
                table: "credit_adjustment_approvals",
                columns: new[] { "organization_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_adjustment_approvals_state_requested_at",
                table: "credit_adjustment_approvals",
                columns: new[] { "state", "requested_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_adjustment_approvals");
        }
    }
}
