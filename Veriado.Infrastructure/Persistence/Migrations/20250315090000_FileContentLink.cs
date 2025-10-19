using Microsoft.EntityFrameworkCore.Migrations;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FileContentLink : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "file_content_link",
            columns: table => new
            {
                file_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                content_version = table.Column<int>(type: "INTEGER", nullable: false),
                provider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                location = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                content_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                size_bytes = table.Column<long>(type: "BIGINT", nullable: false),
                mime = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                created_utc = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_content_link", x => new { x.file_id, x.content_version });
                table.ForeignKey(
                    name: "FK_file_content_link_files_file_id",
                    column: x => x.file_id,
                    principalTable: "files",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "idx_file_content_link_hash",
            table: "file_content_link",
            column: "content_hash");

        migrationBuilder.AddColumn<string>(
            name: "content_provider",
            table: "files",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "content_location",
            table: "files",
            type: "TEXT",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "content_size",
            table: "files",
            type: "BIGINT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "content_mime",
            table: "files",
            type: "TEXT",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "content_created_utc",
            table: "files",
            type: "TEXT",
            nullable: true);

        migrationBuilder.Sql(
            @"INSERT INTO file_content_link (
                    file_id,
                    content_version,
                    provider,
                    location,
                    content_hash,
                    size_bytes,
                    mime,
                    created_utc)
              SELECT
                    f.id,
                    COALESCE(f.content_version, 1),
                    'local',
                    COALESCE((SELECT fs.path FROM filesystem_entities fs WHERE fs.id = f.filesystem_id), 'legacy/' || hex(f.id)),
                    f.content_hash,
                    COALESCE((SELECT fs.size FROM filesystem_entities fs WHERE fs.id = f.filesystem_id), f.size_bytes, 0),
                    f.mime,
                    f.created_utc
              FROM files f;");

        migrationBuilder.Sql(
            @"UPDATE files
                SET content_provider = 'local',
                    content_location = COALESCE((SELECT fs.path FROM filesystem_entities fs WHERE fs.id = files.filesystem_id), 'legacy/' || hex(files.id)),
                    content_size = COALESCE((SELECT fs.size FROM filesystem_entities fs WHERE fs.id = files.filesystem_id), files.size_bytes, 0),
                    content_mime = files.mime,
                    content_created_utc = files.created_utc
              WHERE content_provider IS NULL;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "file_content_link");

        migrationBuilder.DropColumn(
            name: "content_provider",
            table: "files");

        migrationBuilder.DropColumn(
            name: "content_location",
            table: "files");

        migrationBuilder.DropColumn(
            name: "content_size",
            table: "files");

        migrationBuilder.DropColumn(
            name: "content_mime",
            table: "files");

        migrationBuilder.DropColumn(
            name: "content_created_utc",
            table: "files");
    }
}
