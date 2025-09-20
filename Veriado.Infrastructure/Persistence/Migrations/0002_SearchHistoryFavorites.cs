using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Persistence.Migrations
{
    public partial class _0002_SearchHistoryFavorites : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "search_history",
                columns: table => new
                {
                    id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    query_text = table.Column<string>(type: "TEXT", nullable: true),
                    match = table.Column<string>(type: "TEXT", nullable: false),
                    created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    executions = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    last_total_hits = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_history", x => x.id);
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
                name: "idx_search_favorites_position",
                table: "search_favorites",
                column: "position");

            migrationBuilder.CreateIndex(
                name: "ux_search_favorites_name",
                table: "search_favorites",
                column: "name",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "search_favorites");

            migrationBuilder.DropTable(
                name: "search_history");
        }
    }
}
