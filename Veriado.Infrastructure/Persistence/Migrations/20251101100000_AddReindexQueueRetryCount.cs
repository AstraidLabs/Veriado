using Microsoft.EntityFrameworkCore.Migrations;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddReindexQueueRetryCount : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "retry_count",
            table: "reindex_queue",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "retry_count",
            table: "reindex_queue");
    }
}
