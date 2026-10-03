using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helios.Infrastructure.Persistence.MySql.Migrations
{
    /// <inheritdoc />
    public partial class ReviewDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "review_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    api_request_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    organization_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    workspace_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    decision = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    corrections_json = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    actor_user_id = table.Column<Guid>(type: "binary(16)", nullable: true),
                    api_key_id = table.Column<Guid>(type: "binary(16)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    purged_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_review_decisions_api_requests_api_request_id",
                        column: x => x.api_request_id,
                        principalTable: "api_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_review_decisions_api_request_id_created_at",
                table: "review_decisions",
                columns: new[] { "api_request_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_review_decisions_workspace_id",
                table: "review_decisions",
                column: "workspace_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_decisions");
        }
    }
}
