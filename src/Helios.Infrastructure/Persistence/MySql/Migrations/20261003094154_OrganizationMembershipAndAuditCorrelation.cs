using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helios.Infrastructure.Persistence.MySql.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationMembershipAndAuditCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "audit_logs",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "audit_logs",
                type: "binary(16)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "organization_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    organization_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    user_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    role = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "binary(16)", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<Guid>(type: "binary(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_members_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_correlation_id",
                table: "audit_logs",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_organization_id_occurred_at",
                table: "audit_logs",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_organization_members_organization_id_user_id",
                table: "organization_members",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_members_user_id",
                table: "organization_members",
                column: "user_id");

            // Backfill so existing companies keep an administrator once membership is enforced.
            // 1. Whoever created the organisation becomes its Owner.
            migrationBuilder.Sql("""
                INSERT INTO organization_members (id, organization_id, user_id, role, is_active, created_at, created_by)
                SELECT UUID_TO_BIN(UUID(), 1), o.id, o.created_by, 'Owner', 1, UTC_TIMESTAMP(6), NULL
                FROM organizations AS o
                WHERE o.created_by IS NOT NULL
                  AND EXISTS (SELECT 1 FROM users AS u WHERE u.id = o.created_by);
                """);

            // 2. Existing workspace members keep access to their company's workspaces: Admin if
            //    they held Owner/Admin on any of its workspaces, otherwise Operator. Neither role
            //    can grant Owner or manage billing; an Owner reviews these after upgrade.
            migrationBuilder.Sql("""
                INSERT INTO organization_members (id, organization_id, user_id, role, is_active, created_at, created_by)
                SELECT UUID_TO_BIN(UUID(), 1), x.organization_id, x.user_id,
                       CASE WHEN x.is_admin = 1 THEN 'Admin' ELSE 'Operator' END,
                       1, UTC_TIMESTAMP(6), NULL
                FROM (
                    SELECT w.organization_id, wm.user_id, MAX(wm.role IN ('Owner', 'Admin')) AS is_admin
                    FROM workspace_members AS wm
                    INNER JOIN workspaces AS w ON w.id = wm.workspace_id
                    GROUP BY w.organization_id, wm.user_id
                ) AS x
                WHERE NOT EXISTS (
                    SELECT 1 FROM organization_members AS m
                    WHERE m.organization_id = x.organization_id AND m.user_id = x.user_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_members");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_correlation_id",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_organization_id_occurred_at",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "audit_logs");
        }
    }
}
