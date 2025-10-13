# FTS5 mini benchmark

Syntetický test byl spuštěn nad dočasnou databází `tools/fts5-bench.db` vytvořenou podle produkčního schématu. Měření proběhlo s aktivními PRAGMA nastaveními `journal_mode=WAL`, `synchronous=NORMAL`, `temp_store=MEMORY`, `mmap_size=268435456` a `cache_size=-32768`. Batch indexace proběhla v jediné transakci s následným rollbackem, aby se nezměnil stav databáze.

## Výsledky
- Propustnost dávkové indexace: **33 980 řádků/s** při 400 vložených dokumentech (11,77 ms na dávku).【b1d185†L1-L5】
- Dotaz `report`: p50 = **0,02 ms**, p95 = **0,03 ms** (50 vzorků).【b1d185†L1-L5】
- Dotaz `author:"Author 03"`: p50 = **0,02 ms**, p95 = **0,03 ms** (50 vzorků).【b1d185†L1-L5】
- Dotaz `title:quarterly`: p50 = **0,02 ms**, p95 = **0,02 ms** (50 vzorků).【b1d185†L1-L5】

Poznámka: Benchmark využívá pouze metadata a neobsahuje plnohodnotný text dokumentů; výsledky proto slouží primárně pro regresní porovnání výkonu indexace a MATCH dotazů.
