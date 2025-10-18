using Microsoft.EntityFrameworkCore.Migrations;
using Veriado.Infrastructure.Persistence.Schema;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class Fts5ContentlessUnified : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var statement in SqliteFulltextSchemaSql.ResetStatements)
        {
            migrationBuilder.Sql(statement);
        }

        foreach (var statement in SqliteFulltextSchemaSql.CreateStatements)
        {
            migrationBuilder.Sql(statement);
        }

        migrationBuilder.Sql(SqliteFulltextSchemaSql.PopulateStatement);
        migrationBuilder.Sql(SqliteFulltextSchemaSql.RebuildStatement);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS sd_ai;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS sd_au;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS sd_ad;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS search_document_fts;");

        migrationBuilder.Sql(
            @"CREATE VIRTUAL TABLE IF NOT EXISTS search_document_fts
USING fts5(
    title,
    author,
    mime,
    metadata_text,
    content='search_document',
    content_rowid='rowid'
);");

        migrationBuilder.Sql(
            @"CREATE TRIGGER IF NOT EXISTS sd_ai AFTER INSERT ON search_document BEGIN
    INSERT INTO search_document_fts(rowid, title, author, mime, metadata_text)
    VALUES (new.rowid, new.title, new.author, new.mime, new.metadata_text);
END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER IF NOT EXISTS sd_au AFTER UPDATE ON search_document BEGIN
    UPDATE search_document_fts
       SET title=new.title, author=new.author, mime=new.mime, metadata_text=new.metadata_text
     WHERE rowid=new.rowid;
END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER IF NOT EXISTS sd_ad AFTER DELETE ON search_document BEGIN
    DELETE FROM search_document_fts WHERE rowid=old.rowid;
END;");
    }
}
