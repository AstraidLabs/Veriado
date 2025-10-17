DROP TABLE IF EXISTS file_trgm;

CREATE TABLE IF NOT EXISTS search_document (
    file_id BLOB PRIMARY KEY,
    title TEXT NULL,
    author TEXT NULL,
    mime TEXT NOT NULL,
    metadata_text TEXT NULL,
    metadata_json TEXT NULL,
    created_utc TEXT NULL,
    modified_utc TEXT NULL,
    content_hash TEXT NULL
);

CREATE VIRTUAL TABLE IF NOT EXISTS search_document_fts USING fts5(
    title,
    author,
    mime,
    metadata_text,
    metadata,
    content='',
    tokenize='unicode61 remove_diacritics 2'
);

CREATE TRIGGER IF NOT EXISTS sd_ai AFTER INSERT ON search_document BEGIN
  INSERT INTO search_document_fts(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.rowid, new.title, new.author, new.mime, new.metadata_text, new.metadata_json);
END;

CREATE TRIGGER IF NOT EXISTS sd_au AFTER UPDATE ON search_document BEGIN
  INSERT INTO search_document_fts(search_document_fts, rowid, title, author, mime, metadata_text, metadata)
  VALUES ('delete', old.rowid, old.title, old.author, old.mime, old.metadata_text, old.metadata_json);
  INSERT INTO search_document_fts(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.rowid, new.title, new.author, new.mime, new.metadata_text, new.metadata_json);
END;

CREATE TRIGGER IF NOT EXISTS sd_ad AFTER DELETE ON search_document BEGIN
  INSERT INTO search_document_fts(search_document_fts, rowid, title, author, mime, metadata_text, metadata)
  VALUES ('delete', old.rowid, old.title, old.author, old.mime, old.metadata_text, old.metadata_json);
END;

CREATE TABLE IF NOT EXISTS fts_write_ahead (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_id TEXT NOT NULL,
    op TEXT NOT NULL,
    content_hash TEXT NULL,
    title_hash TEXT NULL,
    enqueued_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fts_write_ahead_enqueued ON fts_write_ahead(enqueued_utc);

CREATE TABLE IF NOT EXISTS fts_write_ahead_dlq (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    original_id INTEGER NOT NULL,
    file_id TEXT NOT NULL,
    op TEXT NOT NULL,
    content_hash TEXT NULL,
    title_hash TEXT NULL,
    enqueued_utc TEXT NOT NULL,
    dead_lettered_utc TEXT NOT NULL,
    error TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_fts_write_ahead_dlq_dead_lettered ON fts_write_ahead_dlq(dead_lettered_utc);

-- Variant B (content-linked FTS) reference:
-- CREATE VIRTUAL TABLE file_search USING fts5(
--     title,
--     author,
--     mime,
--     metadata_text,
--     metadata,
--     content='DocumentContent',
--     content_rowid='DocId',
--     tokenize='unicode61 remove_diacritics 2'
-- );
