using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_FtsWriteAhead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "idx_fts_write_ahead_enqueued",
                table: "fts_write_ahead",
                column: "enqueued_utc");

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

            migrationBuilder.CreateIndex(
                name: "idx_fts_write_ahead_dlq_dead_lettered",
                table: "fts_write_ahead_dlq",
                column: "dead_lettered_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fts_write_ahead_dlq");

            migrationBuilder.DropTable(
                name: "fts_write_ahead");
        }
    }
}
