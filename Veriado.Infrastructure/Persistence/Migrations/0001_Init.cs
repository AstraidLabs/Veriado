using System;
using System.IO;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Persistence.Migrations
{
    public partial class _0001_Init : Migration
    {
        private const string Fts5SchemaResourceName = "Veriado.Infrastructure.Persistence.Schema.Fts5.sql";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    extension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    mime = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    author = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    size_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    modified_utc = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    is_read_only = table.Column<bool>(type: "INTEGER", nullable: false),
                    fts_policy = table.Column<string>(type: "TEXT", nullable: false),
                    metadata_json = table.Column<string>(type: "TEXT", nullable: true),
                    fs_attr = table.Column<int>(type: "INTEGER", nullable: false),
                    fs_created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    fs_write_utc = table.Column<string>(type: "TEXT", nullable: false),
                    fs_access_utc = table.Column<string>(type: "TEXT", nullable: false),
                    fs_owner_sid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    fs_links = table.Column<long>(type: "INTEGER", nullable: true),
                    fs_ads = table.Column<long>(type: "INTEGER", nullable: true),
                    fts_schema_version = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    fts_is_stale = table.Column<bool>(type: "INTEGER", nullable: false),
                    fts_last_indexed_utc = table.Column<string>(type: "TEXT", nullable: true),
                    fts_indexed_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    fts_indexed_title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_file",
                columns: table => new
                {
                    file_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    occurred_utc = table.Column<string>(type: "TEXT", nullable: false),
                    action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_file", x => new { x.file_id, x.occurred_utc });
                });

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
                    issued_at = table.Column<string>(type: "TEXT", nullable: true),
                    valid_until = table.Column<string>(type: "TEXT", nullable: true),
                    has_physical = table.Column<bool>(type: "INTEGER", nullable: false),
                    has_electronic = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_file_validity", x => new { x.file_id, x.occurred_utc });
                });

            migrationBuilder.CreateTable(
                name: "file_ext_metadata",
                columns: table => new
                {
                    file_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    fmtid = table.Column<byte[]>(type: "BLOB", nullable: false),
                    pid = table.Column<int>(type: "INTEGER", nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    value_text = table.Column<string>(type: "TEXT", nullable: true),
                    value_blob = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_ext_metadata", x => new { x.file_id, x.fmtid, x.pid });
                });

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

            migrationBuilder.CreateTable(
                name: "files_validity",
                columns: table => new
                {
                    file_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    issued_at = table.Column<string>(type: "TEXT", nullable: true),
                    valid_until = table.Column<string>(type: "TEXT", nullable: true),
                    has_physical = table.Column<bool>(type: "INTEGER", nullable: false),
                    has_electronic = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files_validity", x => x.file_id);
                    table.ForeignKey(
                        name: "FK_files_validity_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false),
                    created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    processed_utc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_files_name",
                table: "files",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "idx_files_mime",
                table: "files",
                column: "mime");

            migrationBuilder.CreateIndex(
                name: "ux_files_content_hash",
                table: "files_content",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_file_ext_metadata_file",
                table: "file_ext_metadata",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "idx_outbox_processed",
                table: "outbox_events",
                column: "processed_utc");

            migrationBuilder.Sql(ReadEmbeddedSql(Fts5SchemaResourceName));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "audit_file");
            migrationBuilder.DropTable(name: "audit_file_content");
            migrationBuilder.DropTable(name: "audit_file_validity");
            migrationBuilder.DropTable(name: "file_ext_metadata");
            migrationBuilder.DropTable(name: "files_content");
            migrationBuilder.DropTable(name: "files_validity");
            migrationBuilder.DropTable(name: "outbox_events");
            migrationBuilder.DropTable(name: "files");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_map;");
        }

        private static string ReadEmbeddedSql(string resourceName)
        {
            var assembly = typeof(_0001_Init).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
            {
                var availableResources = string.Join(", ", assembly.GetManifestResourceNames());
                throw new FileNotFoundException($"Embedded SQL resource '{resourceName}' was not found. Available resources: {availableResources}");
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
