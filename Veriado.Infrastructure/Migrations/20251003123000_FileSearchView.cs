using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Veriado.Infrastructure.Persistence;

#nullable disable

namespace Veriado.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(AppDbContext))]
[Migration("20251003123000_FileSearchView")]
public partial class FileSearchView : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE VIEW IF NOT EXISTS v_file_search_base AS
SELECT
    f.id                 AS id,
    f.name               AS name,
    f.title              AS title,
    f.author             AS author,
    f.mime               AS mime_type,
    f.extension          AS extension,
    f.size_bytes         AS size_bytes,
    f.created_utc        AS created_utc,
    f.modified_utc       AS modified_utc,
    f.version            AS version,
    f.is_read_only       AS is_read_only,
    f.fts_is_stale       AS fts_is_stale,
    f.fts_last_indexed_utc AS fts_last_indexed_utc,
    f.fts_indexed_title  AS fts_indexed_title,
    f.fts_schema_version AS fts_schema_version,
    f.fts_indexed_hash   AS fts_indexed_hash,
    v.issued_at          AS validity_issued_at,
    v.valid_until        AS validity_valid_until,
    v.has_physical       AS validity_has_physical,
    v.has_electronic     AS validity_has_electronic,
    s.rowid              AS fts_rowid
FROM files f
LEFT JOIN files_validity v ON v.file_id = f.id
LEFT JOIN file_search s ON s.rowid = f.id;
");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS v_file_search_base;");
    }
}
