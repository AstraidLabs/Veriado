using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Veriado.Infrastructure.Persistence;

#nullable disable

namespace Veriado.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(AppDbContext))]
[Migration("20251003124500_FileGridIndexes")]
public partial class FileGridIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_files_modified_utc ON files(modified_utc, id);");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_files_size_bytes ON files(size_bytes, id);");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_files_flags ON files(is_read_only, fts_is_stale, id);");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_files_name_nocase ON files(name COLLATE NOCASE, id);");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_files_author_nocase ON files(author COLLATE NOCASE, id);");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS idx_files_author_nocase;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS idx_files_name_nocase;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS idx_files_flags;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS idx_files_size_bytes;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS idx_files_modified_utc;");
    }
}
