using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Outbox_Attempts_And_Dlq : Migration
    {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "attempts",
            table: "outbox_events",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "outbox_dlq",
            columns: table => new
            {
                id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                outbox_id = table.Column<long>(type: "INTEGER", nullable: false),
                type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                payload = table.Column<string>(type: "TEXT", nullable: false),
                created_utc = table.Column<string>(type: "TEXT", nullable: false),
                dead_lettered_utc = table.Column<string>(type: "TEXT", nullable: false),
                attempts = table.Column<int>(type: "INTEGER", nullable: false),
                error = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_dlq", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "idx_outbox_dlq_dead_lettered",
            table: "outbox_dlq",
            column: "dead_lettered_utc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "outbox_dlq");

        migrationBuilder.DropColumn(
            name: "attempts",
            table: "outbox_events");
    }
    }
}