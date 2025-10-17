using Microsoft.EntityFrameworkCore.Migrations;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAuditProjections : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_file_content");

        migrationBuilder.DropTable(
            name: "audit_file_validity");

        migrationBuilder.AddColumn<string>(
            name: "author",
            table: "audit_file",
            type: "TEXT",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "mime",
            table: "audit_file",
            type: "TEXT",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "title",
            table: "audit_file",
            type: "TEXT",
            maxLength: 300,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "audit_file_link",
            columns: table => new
            {
                file_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                occurred_utc = table.Column<string>(type: "TEXT", nullable: false),
                filesystem_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                version = table.Column<int>(type: "INTEGER", nullable: false),
                hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                size = table.Column<long>(type: "INTEGER", nullable: false),
                mime = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_file_link", x => new { x.file_id, x.occurred_utc });
            });

        migrationBuilder.CreateTable(
            name: "audit_filesystem",
            columns: table => new
            {
                filesystem_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                occurred_utc = table.Column<string>(type: "TEXT", nullable: false),
                action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                path = table.Column<string>(type: "TEXT", nullable: true),
                hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                size = table.Column<long>(type: "INTEGER", nullable: true),
                mime = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                attrs = table.Column<int>(type: "INTEGER", nullable: true),
                owner_sid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                is_encrypted = table.Column<bool>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_filesystem", x => new { x.filesystem_id, x.occurred_utc });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_file_link");

        migrationBuilder.DropTable(
            name: "audit_filesystem");

        migrationBuilder.DropColumn(
            name: "author",
            table: "audit_file");

        migrationBuilder.DropColumn(
            name: "mime",
            table: "audit_file");

        migrationBuilder.DropColumn(
            name: "title",
            table: "audit_file");

        migrationBuilder.CreateTable(
            name: "audit_file_content",
            columns: table => new
            {
                file_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                occurred_utc = table.Column<string>(type: "TEXT", nullable: false),
                new_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_file_content", x => new { x.file_id, x.occurred_utc });
            });

        migrationBuilder.CreateTable(
            name: "audit_file_validity",
            columns: table => new
            {
                file_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                occurred_utc = table.Column<string>(type: "TEXT", nullable: false),
                has_electronic = table.Column<bool>(type: "INTEGER", nullable: false),
                has_physical = table.Column<bool>(type: "INTEGER", nullable: false),
                issued_at = table.Column<string>(type: "TEXT", nullable: true),
                valid_until = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_file_validity", x => new { x.file_id, x.occurred_utc });
            });
    }
}
