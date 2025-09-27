using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "files",
                columns: table => new
                {
                    id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    extension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    mime = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    size_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    author = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    is_read_only = table.Column<bool>(type: "INTEGER", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    modified_utc = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    fts_is_stale = table.Column<bool>(type: "INTEGER", nullable: false),
                    fts_schema_version = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    fts_last_indexed_utc = table.Column<string>(type: "TEXT", nullable: true),
                    fts_indexed_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    fts_indexed_title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    fts_policy = table.Column<string>(type: "TEXT", nullable: false),
                    fs_ads = table.Column<uint>(type: "INTEGER", nullable: true),
                    fs_attr = table.Column<int>(type: "INTEGER", nullable: false),
                    fs_created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    fs_links = table.Column<uint>(type: "INTEGER", nullable: true),
                    fs_access_utc = table.Column<string>(type: "TEXT", nullable: false),
                    fs_write_utc = table.Column<string>(type: "TEXT", nullable: false),
                    fs_owner_sid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => x.key);
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

            migrationBuilder.CreateTable(
                name: "search_favorites",
                columns: table => new
                {
                    id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    query_text = table.Column<string>(type: "TEXT", nullable: true),
                    match = table.Column<string>(type: "TEXT", nullable: false),
                    position = table.Column<int>(type: "INTEGER", nullable: false),
                    created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    is_fuzzy = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_favorites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "search_history",
                columns: table => new
                {
                    id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    query_text = table.Column<string>(type: "TEXT", nullable: true),
                    match = table.Column<string>(type: "TEXT", nullable: false),
                    created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    executions = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    last_total_hits = table.Column<int>(type: "INTEGER", nullable: true),
                    is_fuzzy = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_history", x => x.id);
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
                    issued_at = table.Column<string>(type: "TEXT", nullable: false),
                    valid_until = table.Column<string>(type: "TEXT", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "idx_files_mime",
                table: "files",
                column: "mime");

            migrationBuilder.CreateIndex(
                name: "idx_files_name",
                table: "files",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ux_files_content_hash",
                table: "files_content",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_outbox_processed",
                table: "outbox_events",
                column: "processed_utc");

            migrationBuilder.CreateIndex(
                name: "idx_search_favorites_position",
                table: "search_favorites",
                column: "position");

            migrationBuilder.CreateIndex(
                name: "ux_search_favorites_name",
                table: "search_favorites",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_search_history_created",
                table: "search_history",
                column: "created_utc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_search_history_match",
                table: "search_history",
                column: "match");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_file");

            migrationBuilder.DropTable(
                name: "audit_file_content");

            migrationBuilder.DropTable(
                name: "audit_file_validity");

            migrationBuilder.DropTable(
                name: "files_content");

            migrationBuilder.DropTable(
                name: "files_validity");

            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.DropTable(
                name: "search_favorites");

            migrationBuilder.DropTable(
                name: "search_history");

            migrationBuilder.DropTable(
                name: "files");
        }
    }
}
