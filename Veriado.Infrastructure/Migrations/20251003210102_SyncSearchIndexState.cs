using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncSearchIndexState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_fts_write_ahead_dlq_dead_lettered ON fts_write_ahead_dlq (dead_lettered_utc);");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_fts_write_ahead_enqueued ON fts_write_ahead (enqueued_utc);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_fts_write_ahead_dlq_dead_lettered;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_fts_write_ahead_enqueued;");
        }
    }
}
