# Veriado FTS5 Resilience Audit

## Executive Summary
- SQLite FTS5 indexing is tightly integrated with the EF Core unit of work and surfaces corruption via `SearchIndexCorruptedException`, allowing automatic repair attempts from the write worker.
- Canonical metadata (`SearchIndexState`) is persisted with schema version, hash, title and timestamps, but there is no central idempotency/staleness check that recomputes hashes prior to indexing, so silent drift is undetected.
- Integrity verification only checks for missing/orphaned entries and performs brute-force rebuilds; there is no selective verification against analyzer changes, token hashes or WAL-style journaling of pending operations.
- Telemetry reports index size and query latency, yet there is no alerting/health check pipeline to isolate repeatedly failing items.

## Pillars A–E
| Pillar | Status | Notes |
| --- | --- | --- |
| A. Canonical data & idempotence | Partial | `SearchIndexState` stores schema/hash/title/time but pipeline only consults `IsStale`, lacking recomputation from canonical content. |
| B. Unified transaction DB + FTS | Achieved | Write worker executes EF + FTS mutations inside one SQLite transaction via `SqliteFts5Transactional`; idempotent delete+insert semantics applied. |
| C. Automatic runtime recovery | Partial | Corruption escalates consistently and triggers automatic rebuild, yet repeated payload failures still rely on manual intervention. |
| D. Start/periodic integrity | Partial | Startup check invokes `VerifyAsync` and optional repair, telemetry captures counts but no scheduled audits or alert thresholds. |
| E. Recommended extensions | Missing | No WAL journal, analyzer signature, drift verification or standalone rebuild tooling beyond in-process repair. |

## Key Findings by Pillar
### Pillar A – Canonical data and idempotence
- `SearchIndexState` persists schema version, hash, title, and timestamps, and records staleness transitions on domain changes.
- The write worker only consults `SearchIndex.IsStale`; it does not recompute `Content.Hash`/`Title` to detect divergence, so manual tampering or analyzer changes go unnoticed until a rebuild.
- There is no centralized `NeedsReindex` helper that compares canonical data to indexed state.

### Pillar B – Unified transaction DB + FTS
- Write batches always use the current SQLite transaction and `SqliteFts5Transactional` to apply delete-then-insert semantics, včetně `INSERT OR IGNORE` do map tabulek pro idempotenci.

### Pillar C – Automatic runtime recovery
- `SqliteFts5Transactional` converts SQLite corruption/schema exceptions into `SearchIndexCorruptedException`, which is caught by `WriteWorker` to trigger full repair and retry once.
- Failures not classified as corruption (e.g., invalid payloads) simply log errors and require manual intervention.

### Pillar D – Startup / periodic integrity
- `StartupIntegrityCheck` forces a verification run during infrastructure initialization and optionally triggers `RepairAsync`.
- `DiagnosticsRepository` exposes current journal mode, stale counts, and index size through telemetry gauges, but there is no scheduled job to call `VerifyAsync`/`RepairAsync`, nor alert thresholds wired to telemetry.

### Pillar E – Recommended extensions
- The FTS schema includes only the virtual tables and mapping tables; there is no write-ahead journal table or analyzer signature fields.
- No analyzer version, token hash, or `VerifyAsync` drift comparison exists; rebuild tooling is limited to the in-process repair routine.
- PRAGMA enforcement is applied per-connection but not continuously monitored or checkpointed outside the rebuild path.

## Gap List (highest risk first)
1. **Missing canonical drift detection** – Without recomputing hashes/titles, stale FTS rows remain in sync status even if the FTS data differs; manual edits or analyzer changes silently break search relevance.
2. **Integrity verification limited to presence checks** – `VerifyAsync` cannot detect mismatch between indexed payload and canonical content or analyzer configuration.
3. **Lack of analyzer/token signature** – Analyzer configuration changes are not recorded, so pipeline cannot force schema-wide reindex when tokenization rules update.
4. **Missing automated health/alerting** – Telemetry exists but is not tied to health checks or alert thresholds; PRAGMA compliance is not validated after startup.
5. **No standalone rebuild CLI/safe swap** – All repairs run in-process; long rebuilds block the service and risk timeouts.

## Action Plan
1. **Implement hash-based `NeedsReindexAsync`** – Add service in infrastructure to compute `Content.Hash`/normalized title for candidates and compare with `SearchIndexState`, forcing reindex when mismatched. Integrate into write worker prior to queuing FTS operations.
2. **Extend `VerifyAsync` for drift detection** – Stream file corpus, recompute analyzer tokens and compare to FTS content, with selective reindex for mismatches; surface counts via telemetry.
3. **Persist analyzer signature** – Store `AnalyzerVersion` and `TokenHash` v `SearchIndexState`; bump version when tokenizer configuration changes and mark items stale automatically.
4. **Health checks & PRAGMA monitoring** – Implement hosted integrity auditor to run `VerifyAsync`, emit metrics (stale, drift, WAL backlog), assert PRAGMA values, and trigger alerts when thresholds exceeded.
5. **Standalone rebuild CLI** – Provide CLI command that snapshots canonical DB, rebuilds FTS in isolation, and swaps files atomically to reduce downtime risk.

## Suggested Snippets
- `SearchIndexState` extension to include analyzer signature and central `NeedsReindex` logic comparing stored hashes/versions.
- Schema addition for `fts_write_ahead` with columns `(id INTEGER PK, file_id BLOB, operation TEXT, payload TEXT, created_utc TEXT)` and startup replay routine.

## Production Checklist
- [ ] Hash/title comparison gating reindex completed and unit-tested.
- [ ] Analyzer signature fields persisted and schema migration applied.
- [ ] Integrity auditor scheduled (startup + periodic) with alerts on drift/missing/orphans.
- [ ] FTS write-ahead journal + replay implemented and validated under crash simulations.
- [ ] Dedicated rebuild CLI documented and tested with safe file swap.
- [ ] PRAGMA enforcement/verification alerting active; WAL checkpoints automated.
