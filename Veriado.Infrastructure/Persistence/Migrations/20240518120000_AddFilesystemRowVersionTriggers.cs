using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFilesystemRowVersionTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
UPDATE filesystem_entities
SET row_version = randomblob(8)
WHERE row_version IS NULL OR length(row_version) = 0;
""");

            migrationBuilder.Sql("""
DROP TRIGGER IF EXISTS trg_fs_rowversion_insert;
CREATE TRIGGER trg_fs_rowversion_insert
AFTER INSERT ON filesystem_entities
FOR EACH ROW
WHEN NEW.row_version IS NULL OR length(NEW.row_version) = 0
BEGIN
    UPDATE filesystem_entities
    SET row_version = randomblob(8)
    WHERE rowid = NEW.rowid;
END;
""");

            migrationBuilder.Sql("""
DROP TRIGGER IF EXISTS trg_fs_rowversion_update;
CREATE TRIGGER trg_fs_rowversion_update
AFTER UPDATE ON filesystem_entities
FOR EACH ROW
BEGIN
    UPDATE filesystem_entities
    SET row_version = randomblob(8)
    WHERE rowid = NEW.rowid;
END;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_fs_rowversion_insert;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_fs_rowversion_update;");
        }

        // Verification SQL:
        // PRAGMA table_info('filesystem_entities');
        // SELECT name, tbl_name, sql
        // FROM sqlite_master
        // WHERE type='trigger' AND tbl_name='filesystem_entities';
        // -- quick manual test: vlož jeden řádek „natvrdo“ a zkontroluj row_version
        // -- (pouze pokud víš, co děláš – jinak testuj přes aplikaci).
    }
}
