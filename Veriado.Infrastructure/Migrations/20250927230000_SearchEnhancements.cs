using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SearchEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "synonyms",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    lang = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    term = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    variant = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_synonyms", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_synonyms_term",
                table: "synonyms",
                columns: new[] { "lang", "term" });

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

            migrationBuilder.CreateIndex(
                name: "idx_suggestions_lookup",
                table: "suggestions",
                columns: new[] { "lang", "term" });

            migrationBuilder.CreateIndex(
                name: "ux_suggestions_term",
                table: "suggestions",
                columns: new[] { "term", "lang", "source_field" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "idx_document_locations_geo",
                table: "document_locations",
                columns: new[] { "lat", "lon" });

            migrationBuilder.Sql(
                "CREATE VIRTUAL TABLE IF NOT EXISTS suggestions_fts USING fts5(term, lang, source_field, content='suggestions', content_rowid='id', tokenize='unicode61 remove_diacritics 2');");
            migrationBuilder.Sql(
                "CREATE TRIGGER IF NOT EXISTS suggestions_ai AFTER INSERT ON suggestions BEGIN INSERT INTO suggestions_fts(rowid, term, lang, source_field) VALUES (new.id, new.term, new.lang, new.source_field); END;");
            migrationBuilder.Sql(
                "CREATE TRIGGER IF NOT EXISTS suggestions_ad AFTER DELETE ON suggestions BEGIN INSERT INTO suggestions_fts(suggestions_fts, rowid, term, lang, source_field) VALUES('delete', old.id, old.term, old.lang, old.source_field); END;");
            migrationBuilder.Sql(
                "CREATE TRIGGER IF NOT EXISTS suggestions_au AFTER UPDATE ON suggestions BEGIN INSERT INTO suggestions_fts(suggestions_fts, rowid, term, lang, source_field) VALUES('delete', old.id, old.term, old.lang, old.source_field); INSERT INTO suggestions_fts(rowid, term, lang, source_field) VALUES (new.id, new.term, new.lang, new.source_field); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS suggestions_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS suggestions_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS suggestions_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS suggestions_fts;");

            migrationBuilder.DropTable(
                name: "document_locations");

            migrationBuilder.DropTable(
                name: "suggestions");

            migrationBuilder.DropTable(
                name: "synonyms");
        }
    }
}
