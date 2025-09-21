CREATE VIRTUAL TABLE IF NOT EXISTS file_search USING fts5(
    title,
    mime,
    author,
    content,
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
