using Microsoft.EntityFrameworkCore.Migrations;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class Drop_FilesContent : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ux_files_content_hash;");

        migrationBuilder.DropTable(
            name: "files_content");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "files_content",
            columns: table => new
            {
                file_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                bytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_files_content", x => x.file_id);
                table.ForeignKey(
                    name: "FK_files_content_files_file_id",
                    column: x => x.file_id,
                    principalTable: "files",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ux_files_content_hash",
            table: "files_content",
            column: "hash",
            unique: true);

        migrationBuilder.Sql(
            @"INSERT INTO files_content (file_id, bytes, hash)
SELECT id, zeroblob(0), content_hash
FROM files
WHERE NOT EXISTS (
    SELECT 1 FROM files_content existing WHERE existing.file_id = files.id
);");
    }
}
