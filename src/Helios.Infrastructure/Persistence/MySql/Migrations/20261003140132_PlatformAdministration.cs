using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helios.Infrastructure.Persistence.MySql.Migrations
{
    /// <inheritdoc />
    public partial class PlatformAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    user_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    role = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    totp_secret_envelope = table.Column<byte[]>(type: "varbinary(256)", maxLength: 256, nullable: true),
                    totp_key_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    totp_confirmed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    last_totp_step = table.Column<long>(type: "bigint", nullable: true),
                    failed_code_count = table.Column<int>(type: "int", nullable: false),
                    code_locked_until = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "binary(16)", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<Guid>(type: "binary(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_staff", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_staff_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_platform_staff_user_id",
                table: "platform_staff",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_staff");
        }
    }
}
