using System.Collections.Generic;

namespace Veriado.Infrastructure.Persistence.Schema;

/// <summary>
/// Provides reusable SQL statements for managing the unified contentless FTS5 schema.
/// </summary>
internal static class SqliteFulltextSchemaSql
{
    public static IReadOnlyList<string> ResetStatements { get; } = new[]
    {
        "DROP TRIGGER IF EXISTS sd_ai;",
        "DROP TRIGGER IF EXISTS sd_au;",
        "DROP TRIGGER IF EXISTS sd_ad;",
        "DROP TABLE IF EXISTS search_document_fts;",
        "DROP TABLE IF EXISTS file_search;",
        "DROP TABLE IF EXISTS file_search_data;",
        "DROP TABLE IF EXISTS file_search_idx;",
        "DROP TABLE IF EXISTS file_search_content;",
        "DROP TABLE IF EXISTS file_search_docsize;",
        "DROP TABLE IF EXISTS file_search_config;",
        "DROP TABLE IF EXISTS file_trgm;",
    };

    public static IReadOnlyList<string> FullResetStatements { get; } = new[]
    {
        "DROP TRIGGER IF EXISTS sd_ai;",
        "DROP TRIGGER IF EXISTS sd_au;",
        "DROP TRIGGER IF EXISTS sd_ad;",
        "DROP TRIGGER IF EXISTS dc_ai;",
        "DROP TRIGGER IF EXISTS dc_au;",
        "DROP TRIGGER IF EXISTS dc_ad;",
        "DROP TABLE IF EXISTS search_document_fts;",
        "DROP TABLE IF EXISTS file_search;",
        "DROP TABLE IF EXISTS file_search_data;",
        "DROP TABLE IF EXISTS file_search_idx;",
        "DROP TABLE IF EXISTS file_search_content;",
        "DROP TABLE IF EXISTS file_search_docsize;",
        "DROP TABLE IF EXISTS file_search_config;",
        "DROP TABLE IF EXISTS file_trgm;",
        "DROP TABLE IF EXISTS search_document;",
        "DROP TABLE IF EXISTS DocumentContent;",
        "DROP TABLE IF EXISTS fts_write_ahead;",
        "DROP TABLE IF EXISTS fts_write_ahead_dlq;",
    };

    public static IReadOnlyList<string> CreateStatements { get; } = new[]
    {
        @"CREATE TABLE IF NOT EXISTS search_document (
    file_id        BLOB PRIMARY KEY,
    title          TEXT,
    author         TEXT,
    mime           TEXT NOT NULL,
    metadata_text  TEXT,
    metadata_json  TEXT,
    created_utc    TEXT,
    modified_utc   TEXT,
    content_hash   TEXT
);",
        "CREATE INDEX IF NOT EXISTS idx_search_document_mime ON search_document(mime);",
        "CREATE INDEX IF NOT EXISTS idx_search_document_modified ON search_document(modified_utc DESC);",
        @"CREATE VIRTUAL TABLE IF NOT EXISTS search_document_fts USING fts5(
    title,
    author,
    mime,
    metadata_text,
    metadata,
    content='',
    tokenize='unicode61 remove_diacritics 2'
);",
        @"CREATE TRIGGER IF NOT EXISTS sd_ai AFTER INSERT ON search_document BEGIN
  INSERT INTO search_document_fts(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.rowid, new.title, new.author, new.mime, new.metadata_text, new.metadata_json);
END;",
        @"CREATE TRIGGER IF NOT EXISTS sd_au AFTER UPDATE ON search_document BEGIN
  INSERT INTO search_document_fts(search_document_fts, rowid, title, author, mime, metadata_text, metadata)
  VALUES ('delete', old.rowid, old.title, old.author, old.mime, old.metadata_text, old.metadata_json);
  INSERT INTO search_document_fts(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.rowid, new.title, new.author, new.mime, new.metadata_text, new.metadata_json);
END;",
        @"CREATE TRIGGER IF NOT EXISTS sd_ad AFTER DELETE ON search_document BEGIN
  INSERT INTO search_document_fts(search_document_fts, rowid, title, author, mime, metadata_text, metadata)
  VALUES ('delete', old.rowid, old.title, old.author, old.mime, old.metadata_text, old.metadata_json);
END;",
    };

    public static string PopulateStatement { get; } =
        @"INSERT INTO search_document_fts(rowid, title, author, mime, metadata_text, metadata)
SELECT rowid, title, author, mime, metadata_text, metadata_json
FROM search_document;";

    public static string RebuildStatement { get; } =
        "INSERT INTO search_document_fts(search_document_fts) VALUES('rebuild');";
}
