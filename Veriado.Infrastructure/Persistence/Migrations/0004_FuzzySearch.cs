using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Persistence.Migrations
{
    public partial class _0004_FuzzySearch : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_fuzzy",
                table: "search_history",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_fuzzy",
                table: "search_favorites",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "CREATE VIRTUAL TABLE IF NOT EXISTS file_trgm USING fts5(trgm, tokenize='unicode61 remove_diacritics 2', content='', columnsize=0);");

            migrationBuilder.Sql(
                "CREATE TABLE IF NOT EXISTS file_trgm_map (rowid INTEGER PRIMARY KEY, file_id BLOB NOT NULL UNIQUE);");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_file_trgm_map_file ON file_trgm_map(file_id);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_trgm;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS file_trgm_map;");

            migrationBuilder.DropColumn(
                name: "is_fuzzy",
                table: "search_history");

            migrationBuilder.DropColumn(
                name: "is_fuzzy",
                table: "search_favorites");
        }
    }
}
