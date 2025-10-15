DROP TABLE IF EXISTS file_trgm;

CREATE TABLE IF NOT EXISTS DocumentContent (
    doc_id INTEGER PRIMARY KEY,
    file_id BLOB NOT NULL UNIQUE,
    title TEXT NULL,
    author TEXT NULL,
    mime TEXT NOT NULL,
    metadata_text TEXT NULL,
    metadata TEXT NULL
);

CREATE VIRTUAL TABLE IF NOT EXISTS file_search USING fts5(
    title,
    author,
    mime,
    metadata_text,
    metadata,
    tokenize='unicode61 remove_diacritics 2'
);

CREATE TRIGGER IF NOT EXISTS dc_ai AFTER INSERT ON DocumentContent BEGIN
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.doc_id, new.title, new.author, new.mime, new.metadata_text, new.metadata);
END;

CREATE TRIGGER IF NOT EXISTS dc_au AFTER UPDATE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.doc_id);
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES(new.doc_id, new.title, new.author, new.mime, new.metadata_text, new.metadata);
END;

CREATE TRIGGER IF NOT EXISTS dc_ad AFTER DELETE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid)
  VALUES('delete', old.doc_id);
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
