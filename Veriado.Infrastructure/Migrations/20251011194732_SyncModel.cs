using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_dlq");

            migrationBuilder.DropTable(
                name: "outbox_events");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_dlq",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    dead_lettered_utc = table.Column<string>(type: "TEXT", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: false),
                    outbox_id = table.Column<long>(type: "INTEGER", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_dlq", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    created_utc = table.Column<string>(type: "TEXT", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false),
                    processed_utc = table.Column<string>(type: "TEXT", nullable: true),
                    type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_outbox_dlq_dead_lettered",
                table: "outbox_dlq",
                column: "dead_lettered_utc");

            migrationBuilder.CreateIndex(
                name: "idx_outbox_processed",
                table: "outbox_events",
                column: "processed_utc");
        }
    }
}
