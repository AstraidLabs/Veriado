CREATE VIRTUAL TABLE IF NOT EXISTS file_search USING fts5(
    title,
    mime,
    author,
    metadata_text,
    metadata,
    tokenize = 'unicode61 remove_diacritics 2',
    content='',
    columnsize=0
);

CREATE TABLE IF NOT EXISTS file_search_map (
    rowid INTEGER PRIMARY KEY,
    file_id BLOB NOT NULL UNIQUE
);

CREATE VIRTUAL TABLE IF NOT EXISTS file_trgm
USING fts5(
    trgm,
    tokenize = 'unicode61 remove_diacritics 2',
    content='',
    columnsize=0
);

CREATE TABLE IF NOT EXISTS file_trgm_map (
    rowid INTEGER PRIMARY KEY,
    file_id BLOB NOT NULL UNIQUE
);

CREATE INDEX IF NOT EXISTS idx_file_trgm_map_file ON file_trgm_map(file_id);

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
