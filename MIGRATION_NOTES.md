# Migration Notes

## 2025-FTS rebuild hardening

- Full-text rebuild now deploys the contentless `file_search` FTS5 variant with manual triggers keeping it in sync with `DocumentContent`.
- The rebuild pipeline replaces checkpoint file deletion with `PRAGMA wal_checkpoint(TRUNCATE)` for safe WAL maintenance.
- To experiment with the legacy `content='DocumentContent'` configuration, uncomment the reference block in `Veriado.Infrastructure/Persistence/Schema/Fts5.sql` and adjust the triggers to include the row payload.
