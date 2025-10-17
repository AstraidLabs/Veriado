using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainEventLogAndReindexQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.CreateTable(
                name: "domain_event_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    event_type = table.Column<string>(type: "TEXT", nullable: false),
                    event_json = table.Column<string>(type: "TEXT", nullable: false),
                    aggregate_id = table.Column<string>(type: "TEXT", nullable: false),
                    occurred_utc = table.Column<string>(type: "TEXT", nullable: false),
                    processed_utc = table.Column<string>(type: "TEXT", nullable: true),
                    retry_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain_event_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reindex_queue",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    file_id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    reason = table.Column<int>(type: "INTEGER", nullable: false),
                    enqueued_utc = table.Column<string>(type: "TEXT", nullable: false),
                    processed_utc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reindex_queue", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_domain_event_log_processed",
                table: "domain_event_log",
                column: "processed_utc");

            migrationBuilder.CreateIndex(
                name: "idx_reindex_queue_unprocessed",
                table: "reindex_queue",
                column: "processed_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "domain_event_log");

            migrationBuilder.DropTable(
                name: "reindex_queue");

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

            migrationBuilder.CreateIndex(
                name: "idx_outbox_created",
                table: "outbox_events",
                column: "created_utc");

            migrationBuilder.CreateIndex(
                name: "idx_outbox_attempts",
                table: "outbox_events",
                column: "attempts");
        }
    }
}
