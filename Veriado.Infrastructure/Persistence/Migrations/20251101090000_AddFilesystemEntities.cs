using Microsoft.EntityFrameworkCore.Migrations;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddFilesystemEntities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "filesystem_entities",
            columns: table => new
            {
                id = table.Column<byte[]>(type: "BLOB", nullable: false),
                provider = table.Column<int>(type: "INTEGER", nullable: false),
                path = table.Column<string>(type: "TEXT", nullable: false),
                hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                size = table.Column<long>(type: "BIGINT", nullable: false),
                mime = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                attributes = table.Column<int>(type: "INTEGER", nullable: false),
                owner_sid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                is_encrypted = table.Column<bool>(type: "INTEGER", nullable: false),
                is_missing = table.Column<bool>(type: "INTEGER", nullable: false),
                missing_since_utc = table.Column<string>(type: "TEXT", nullable: true),
                content_version = table.Column<int>(type: "INTEGER", nullable: false),
                created_utc = table.Column<string>(type: "TEXT", nullable: false),
                last_write_utc = table.Column<string>(type: "TEXT", nullable: false),
                last_access_utc = table.Column<string>(type: "TEXT", nullable: false),
                last_linked_utc = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_filesystem_entities", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ux_filesystem_entities_path",
            table: "filesystem_entities",
            column: "path",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "idx_filesystem_entities_hash",
            table: "filesystem_entities",
            column: "hash");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "filesystem_entities");
    }
}
