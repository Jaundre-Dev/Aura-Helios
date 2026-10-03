using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helios.Infrastructure.Persistence.MySql.Migrations
{
    /// <inheritdoc />
    public partial class SpendingLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "monthly_spend_limit",
                table: "organizations",
                type: "decimal(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "monthly_budget",
                table: "api_keys",
                type: "decimal(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_requests_api_key_id_created_at",
                table: "api_requests",
                columns: new[] { "api_key_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_api_requests_organization_id_created_at",
                table: "api_requests",
                columns: new[] { "organization_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_api_requests_api_key_id_created_at",
                table: "api_requests");

            migrationBuilder.DropIndex(
                name: "ix_api_requests_organization_id_created_at",
                table: "api_requests");

            migrationBuilder.DropColumn(
                name: "monthly_spend_limit",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "monthly_budget",
                table: "api_keys");
        }
    }
}
