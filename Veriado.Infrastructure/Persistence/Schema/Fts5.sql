DROP TABLE IF EXISTS file_trgm;

CREATE TABLE IF NOT EXISTS DocumentContent (
    DocId INTEGER PRIMARY KEY,
    FileId BLOB NOT NULL UNIQUE,
    Title TEXT NULL,
    Author TEXT NULL,
    Mime TEXT NOT NULL,
    MetadataText TEXT NULL,
    Metadata TEXT NULL
);

CREATE VIRTUAL TABLE IF NOT EXISTS file_search USING fts5(
    title,
    author,
    mime,
    metadata_text,
    metadata,
    content='DocumentContent',
    content_rowid='DocId',
    tokenize='unicode61 remove_diacritics 2'
);

CREATE TRIGGER IF NOT EXISTS dc_ai AFTER INSERT ON DocumentContent BEGIN
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES (new.DocId, new.Title, new.Author, new.Mime, new.MetadataText, new.Metadata);
END;

CREATE TRIGGER IF NOT EXISTS dc_au AFTER UPDATE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid, title, author, mime, metadata_text, metadata)
  VALUES('delete', old.DocId, old.Title, old.Author, old.Mime, old.MetadataText, old.Metadata);
  INSERT INTO file_search(rowid, title, author, mime, metadata_text, metadata)
  VALUES(new.DocId, new.Title, new.Author, new.Mime, new.MetadataText, new.Metadata);
END;

CREATE TRIGGER IF NOT EXISTS dc_ad AFTER DELETE ON DocumentContent BEGIN
  INSERT INTO file_search(file_search, rowid, title, author, mime, metadata_text, metadata)
  VALUES('delete', old.DocId, old.Title, old.Author, old.Mime, old.MetadataText, old.Metadata);
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
