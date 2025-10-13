# Search indexing analysis

V aktuální verzi aplikace probíhá indexace i dotazování výhradně nad SQLite FTS5. Hybridní trigramové fallbacky byly odstraněny, což zjednodušilo pipeline i provozní režii.

## Koordinace zápisu
- `SqliteSearchIndexCoordinator` rozhoduje, zda se indexace provede synchronně v rámci stávající transakce (`SameTransaction`), nebo se uloží do fronty write-ahead službou `SqliteFts5Indexer`. Obě cesty používají `SqliteFts5Transactional`, které upsertuje záznam v `DocumentContent` a tím automaticky synchronizuje `file_search` triggery v jedné transakci.【F:Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs†L9-L74】【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L15-L122】【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L12-L154】
- Write-ahead úložiště (`fts_write_ahead`, `fts_write_ahead_dlq`) uchovává neprovedené operace a `FtsWriteAheadService` je přehrává při startu nebo po chybě. Formát zůstává nezměněn, pouze odstranil trigramové payloady.【F:Veriado.Infrastructure/Search/FtsWriteAheadService.cs†L17-L170】

## Konfigurace
- `SearchOptions` a `SearchScoreOptions` definují váhy BM25, analyzéry, facety a návrhy. Konfigurace je zjednodušená na čisté FTS5 bez pomocných sekcí pro fuzzy nebo spell-check funkce.【F:Veriado.Application/Search/SearchOptions.cs†L5-L82】
- `ServiceCollectionExtensions` registruje pouze FTS komponenty – indexer, koordinátor, write-ahead servis, query builder, historii a oblíbené položky. Fuzzy služby byly odstraněny a DI graf je jednodušší.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L56-L151】

## Generování dotazů a výsledků
- `SearchQueryBuilder` kombinuje boolovské, frázové, proximní, prefixové a rozsahové operátory a produkuje `SearchQueryPlan` s čistým FTS MATCH výrazem a doplňkovými filtry. Žádný fallback ani heuristika pro fuzzy vyhledávání není přítomna.【F:Veriado.Application/Search/SearchQueryBuilder.cs†L1-L240】
- `SqliteFts5QueryService` vykonává dotazy, počítá `bm25` podle konfigurovaných vah, váže `file_search` na `DocumentContent` a vrací `SearchHit` se snippety z `metadata_text`. Telemetrie latence je publikována přímo při načítání výsledků.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L1-L336】

## Dopady odstranění trigramů
- Služby historie (`SearchHistoryService`) a oblíbených dotazů (`SearchFavoritesService`) již nepracují s příznakem fuzzy vyhledávání a operují pouze s reálnými sloupci svých tabulek.【F:Veriado.Infrastructure/Search/SearchHistoryService.cs†L16-L87】【F:Veriado.Infrastructure/Search/SearchFavoritesService.cs†L15-L118】
- Integritní nástroje (`IndexAuditor`, `FulltextIntegrityService`) porovnávají obsah `DocumentContent` s doménovými soubory a neobsahují žádnou trigram logiku. Audit i oprava indexu jsou proto přehlednější a rychlejší.【F:Veriado.Infrastructure/Integrity/IndexAuditor.cs†L1-L212】【F:Veriado.Infrastructure/Integrity/FulltextIntegrityService.cs†L60-L430】
- Spellchecker placeholder byl odstraněn; k dispozici zůstává pouze FTS5 indexace a prefixové návrhy. Tím se minimalizuje mrtvý kód a vazby na neexistující trigramová data.【F:Veriado.Application/Search/Abstractions/SearchServices.cs†L1-L232】【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L56-L214】

## Další doporučení
- Zaměřit testy na koncové scénáře indexace a dotazování s čistým FTS, aby se zachytily případné regresní chyby po odstranění fuzzy větví.
- Monitorovat velikost a fragmentaci FTS tabulek (`file_search`, `file_search_data`, `file_search_idx`, `file_search_content`, `file_search_docsize`, `file_search_config`) a pravidelně spouštět `VACUUM` v rámci integrity služby pro udržení výkonu.
