using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ensures legacy databases include optional title columns expected by the current model.
/// </summary>
public partial class _0005_FileTitleCompatibility : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQLite added support for "ADD COLUMN IF NOT EXISTS" in version 3.35.
        // Microsoft.Data.Sqlite bundles a recent SQLite build so this guard is safe
        // and avoids migration failures when the column is already present.
        migrationBuilder.Sql(
            "ALTER TABLE files ADD COLUMN IF NOT EXISTS title TEXT;");

        migrationBuilder.Sql(
            "ALTER TABLE files ADD COLUMN IF NOT EXISTS fts_indexed_title TEXT;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Columns were added conditionally for compatibility. Dropping them in the
        // downgrade path could lead to data loss, so the operation is intentionally
        // left empty.
    }
}
