using Microsoft.EntityFrameworkCore.Migrations;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class UpdateDocumentContentSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS sd_ai;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS sd_au;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS sd_ad;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ai;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_au;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dc_ad;");

        migrationBuilder.Sql("DROP TABLE IF EXISTS search_document_fts;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS file_search;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_data;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_idx;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_content;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_docsize;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS file_search_config;");

        migrationBuilder.Sql("ALTER TABLE DocumentContent RENAME TO search_document_old;");

        migrationBuilder.Sql(
            @"CREATE TABLE search_document (
    file_id BLOB PRIMARY KEY,
    title TEXT NULL,
    author TEXT NULL,
    mime TEXT NOT NULL,
    metadata_text TEXT NULL,
    metadata_json TEXT NULL,
    created_utc TEXT NULL,
    modified_utc TEXT NULL,
    content_hash TEXT NULL
);");

        migrationBuilder.Sql(
            @"INSERT INTO search_document (rowid, file_id, title, author, mime, metadata_text, metadata_json)
SELECT doc_id, file_id, title, author, mime, metadata_text, metadata
FROM search_document_old;");

        migrationBuilder.Sql("DROP TABLE search_document_old;");

        migrationBuilder.Sql(
            @"CREATE VIRTUAL TABLE search_document_fts USING fts5(
    title,
    author,
    mime,
    metadata_text,
    metadata,
    content='',
    tokenize='unicode61 remove_diacritics 2'
);");

        migrationBuilder.Sql(
            @"INSERT INTO search_document_fts(rowid, title, author, mime, metadata_text, metadata)
SELECT rowid, title, author, mime, metadata_text, metadata_json
FROM search_document;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER sd_ai AFTER INSERT ON search_document BEGIN
  INSERT INTO search_document_fts(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.rowid, new.title, new.author, new.mime, new.metadata_text, new.metadata_json);
END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER sd_au AFTER UPDATE ON search_document BEGIN
  INSERT INTO search_document_fts(search_document_fts, rowid, title, author, mime, metadata_text, metadata)
  VALUES ('delete', old.rowid, old.title, old.author, old.mime, old.metadata_text, old.metadata_json);
  INSERT INTO search_document_fts(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.rowid, new.title, new.author, new.mime, new.metadata_text, new.metadata_json);
END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER sd_ad AFTER DELETE ON search_document BEGIN
  INSERT INTO search_document_fts(search_document_fts, rowid, title, author, mime, metadata_text, metadata)
  VALUES ('delete', old.rowid, old.title, old.author, old.mime, old.metadata_text, old.metadata_json);
END;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS sd_ai;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS sd_au;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS sd_ad;");

        migrationBuilder.Sql("DROP TABLE IF EXISTS search_document_fts;");

        migrationBuilder.Sql("ALTER TABLE search_document RENAME TO search_document_new;");

        migrationBuilder.Sql(
            @"CREATE TABLE DocumentContent (
    doc_id INTEGER PRIMARY KEY,
    file_id BLOB NOT NULL UNIQUE,
    title TEXT NULL,
    author TEXT NULL,
    mime TEXT NOT NULL,
    metadata_text TEXT NULL,
    metadata TEXT NULL
);");

        migrationBuilder.Sql(
            @"INSERT INTO DocumentContent (doc_id, file_id, title, author, mime, metadata_text, metadata)
SELECT rowid, file_id, title, author, COALESCE(mime, ''), metadata_text, metadata_json
FROM search_document_new;");

        migrationBuilder.Sql("DROP TABLE search_document_new;");

        migrationBuilder.Sql(
            @"CREATE VIRTUAL TABLE file_search USING fts5(
    title,
    author,
    mime,
    metadata_text,
    metadata,
    tokenize='unicode61 remove_diacritics 2'
);");

        migrationBuilder.Sql(
            @"INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
SELECT doc_id, title, author, mime, metadata_text, metadata
FROM DocumentContent;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER dc_ai AFTER INSERT ON DocumentContent BEGIN
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.doc_id, new.title, new.author, new.mime, new.metadata_text, new.metadata);
END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER dc_au AFTER UPDATE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.doc_id);
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES(new.doc_id, new.title, new.author, new.mime, new.metadata_text, new.metadata);
END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER dc_ad AFTER DELETE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.doc_id);
END;");
    }
}
