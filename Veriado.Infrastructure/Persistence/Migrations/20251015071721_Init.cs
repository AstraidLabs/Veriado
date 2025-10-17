using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Persistence.Migrations
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
                    file_system_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    extension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    mime = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
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
                    fts_analyzer_version = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "v1"),
                    fts_token_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    fts_policy = table.Column<string>(type: "TEXT", nullable: false),
                    fs_ads = table.Column<int>(type: "INTEGER", nullable: true),
                    fs_attr = table.Column<int>(type: "INTEGER", nullable: false),
                    fs_created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    fs_links = table.Column<int>(type: "INTEGER", nullable: true),
                    fs_access_utc = table.Column<string>(type: "TEXT", nullable: false),
                    fs_write_utc = table.Column<string>(type: "TEXT", nullable: false),
                    fs_owner_sid = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fts_write_ahead",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    file_id = table.Column<string>(type: "TEXT", nullable: false),
                    op = table.Column<string>(type: "TEXT", nullable: false),
                    content_hash = table.Column<string>(type: "TEXT", nullable: true),
                    title_hash = table.Column<string>(type: "TEXT", nullable: true),
                    enqueued_utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fts_write_ahead", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fts_write_ahead_dlq",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    original_id = table.Column<long>(type: "INTEGER", nullable: false),
                    file_id = table.Column<string>(type: "TEXT", nullable: false),
                    op = table.Column<string>(type: "TEXT", nullable: false),
                    content_hash = table.Column<string>(type: "TEXT", nullable: true),
                    title_hash = table.Column<string>(type: "TEXT", nullable: true),
                    enqueued_utc = table.Column<string>(type: "TEXT", nullable: false),
                    dead_lettered_utc = table.Column<string>(type: "TEXT", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fts_write_ahead_dlq", x => x.id);
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
                    id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "TEXT", nullable: true)
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
                    created_utc = table.Column<string>(type: "TEXT", nullable: false)
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
                    last_total_hits = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suggestions",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    term = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    weight = table.Column<double>(type: "REAL", nullable: false, defaultValue: 1.0),
                    lang = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, defaultValue: "en"),
                    source_field = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suggestions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_locations",
                columns: table => new
                {
                    file_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    lat = table.Column<double>(type: "REAL", nullable: false),
                    lon = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_locations", x => x.file_id);
                    table.ForeignKey(
                        name: "FK_document_locations_files_file_id",
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
                name: "idx_document_locations_geo",
                table: "document_locations",
                columns: new[] { "lat", "lon" });

            migrationBuilder.CreateIndex(
                name: "idx_files_mime",
                table: "files",
                column: "mime");

            migrationBuilder.CreateIndex(
                name: "idx_files_name",
                table: "files",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "idx_fts_write_ahead_dlq_dead_lettered",
                table: "fts_write_ahead_dlq",
                column: "dead_lettered_utc");

            migrationBuilder.CreateIndex(
                name: "idx_fts_write_ahead_enqueued",
                table: "fts_write_ahead",
                column: "enqueued_utc");

            migrationBuilder.CreateIndex(
                name: "idx_outbox_attempts",
                table: "outbox_events",
                column: "attempts");

            migrationBuilder.CreateIndex(
                name: "idx_outbox_created",
                table: "outbox_events",
                column: "created_utc");

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
                descending: new[] { true });

            migrationBuilder.CreateIndex(
                name: "idx_search_history_match",
                table: "search_history",
                column: "match");

            migrationBuilder.CreateIndex(
                name: "idx_suggestions_lookup",
                table: "suggestions",
                columns: new[] { "lang", "term" });

            migrationBuilder.CreateIndex(
                name: "ux_suggestions_term",
                table: "suggestions",
                columns: new[] { "term", "lang", "source_field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_files_hash",
                table: "files",
                column: "hash",
                unique: true);

            migrationBuilder.Sql(
                @"CREATE TABLE DocumentContent (
    doc_id INTEGER PRIMARY KEY,
    file_id BLOB NOT NULL UNIQUE,
    title TEXT NULL,
    author TEXT NULL,
    mime TEXT NOT NULL,
    metadata_text TEXT NULL,
    metadata TEXT NULL
);");

            migrationBuilder.Sql(
                @"CREATE VIRTUAL TABLE file_search USING fts5(
    title,
    author,
    mime,
    metadata_text,
    metadata,
    tokenize='unicode61 remove_diacritics 2'
);");

            migrationBuilder.Sql(
                @"CREATE TRIGGER dc_ai AFTER INSERT ON DocumentContent BEGIN
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.doc_id, new.title, new.author, new.mime, new.metadata_text, new.metadata);
END;");

            migrationBuilder.Sql(
                @"CREATE TRIGGER dc_au AFTER UPDATE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.doc_id);
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES(new.doc_id, new.title, new.author, new.mime, new.metadata_text, new.metadata);
END;");

            migrationBuilder.Sql(
                @"CREATE TRIGGER dc_ad AFTER DELETE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.doc_id);
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_search;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS DocumentContent;");

            migrationBuilder.DropTable(
                name: "audit_file");

            migrationBuilder.DropTable(
                name: "audit_file_content");

            migrationBuilder.DropTable(
                name: "audit_file_validity");

            migrationBuilder.DropTable(
                name: "document_locations");

            migrationBuilder.DropTable(
                name: "files_validity");

            migrationBuilder.DropTable(
                name: "fts_write_ahead_dlq");

            migrationBuilder.DropTable(
                name: "fts_write_ahead");

            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.DropTable(
                name: "search_favorites");

            migrationBuilder.DropTable(
                name: "search_history");

            migrationBuilder.DropTable(
                name: "suggestions");

            migrationBuilder.DropTable(
                name: "files");
        }
    }
}
