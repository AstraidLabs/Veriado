# Analýza vyhledávání a indexace

Tento dokument shrnuje, jak Veriado provádí indexaci souborů do SQLite FTS5 a trigramového indexu a jak následně vyhodnocuje dotazy.

## Přehled architektury
- **Doménová vrstva.** `FileEntity` při změně stavu proměňuje agregát na `SearchDocument`, který obsahuje normalizovaný titul, MIME typ, autora, souhrn metadat a serializovaný JSON. `MetadataTextFormatter` generuje souhrn z lidsky čitelných atributů, aby bylo k dispozici kvalitní zvýraznění výsledků.【F:Veriado.Domain/Files/FileEntity.cs†L365-L396】【F:Veriado.Domain/Search/SearchDocument.cs†L11-L58】【F:Veriado.Domain/Search/MetadataTextFormatter.cs†L12-L104】
- **Koordinace zápisu.** `SqliteSearchIndexCoordinator` rozhoduje, zda se indexace provede synchronně (`SameTransaction`) nebo asynchronně přes `SqliteFts5Indexer`. Obě cesty využívají `SqliteFts5Transactional`, který pracuje se sdílenou transakcí SQLite.【F:Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs†L9-L74】【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L7-L88】【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L10-L127】
- **Konfigurace.** `SearchOptions` poskytují výchozí váhy pro `bm25`, parametry trigramů i volbu profilů analyzéru. Registrace probíhá přes `ServiceCollectionExtensions`, takže všechny služby dostanou odpovídající `IOptions<T>`.【F:Veriado.Application/Search/SearchOptions.cs†L7-L106】【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L93-L123】

## Indexační pipeline
1. **Mapování identifikátorů.** `SqliteFts5Transactional.IndexAsync` zajišťuje existenci vazeb mezi GUID souboru a `rowid` v mapovacích tabulkách `file_search_map` a `file_trgm_map`. Tím se odděluje logický identifikátor od vnitřního rowid FTS tabulek a usnadňuje reindexaci.【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L28-L108】
2. **Zápis do FTS5.** Metoda provede "delete" marker, aby odstranila starý obsah, a poté vloží normalizované hodnoty titul, MIME, autor, `metadata_text` a JSON `metadata` do tabulky `file_search`. Tokenizer `unicode61` je nakonfigurován bez diakritiky, takže vyhledávání funguje i bez zadání diakritiky.【F:Veriado.Infrastructure/Persistence/Schema/Fts5.sql†L1-L30】【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L32-L70】
3. **Generování trigramů.** Při indexaci se zvolené segmenty (titul, autor, název souboru, metadata_text) poskládají pomocí `TrigramQueryBuilder.BuildIndexEntry`. Proces respektuje limit 2 048 tokenů, aby trigram tabulka nerostla neúměrně u extrémně dlouhých dokumentů.【F:Veriado.Application/Search/TrigramQueryBuilder.cs†L33-L115】【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L48-L109】
4. **Asynchronní úkoly.** Pokud indexace běží mimo transakci entity frameworku, `SqliteFts5Indexer` otevírá vlastní připojení a zajišťuje serializaci práce pomocí `AsyncLock`, aby se předešlo kolizím nad FTS tabulkami.【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L16-L88】

## Vyhodnocování dotazů
- **Sestavení dotazu.** `SearchQueryBuilder` kombinuje boolovské, frázové, rozsahové i prefixové operátory. Při kompilaci dotazu se uplatní synonyma a profil analyzéru z `SearchOptions`. Výsledek je plán (`SearchPlan`), který sděluje, zda je nutný fallback na trigramy.【F:Veriado.Application/Search/SearchQueryBuilder.cs†L9-L266】
- **Hybridní strategie.** `HybridSearchQueryService` podle plánu spouští FTS dotaz (`SqliteFts5QueryService`) a v případě potřeby trigramovou aproximaci. Váhy FTS vs. trigramy se mísí podle konfigurace (`WeightedFts`, `DefaultTrigramScale`, `TrigramFloor`).【F:Veriado.Infrastructure/Search/HybridSearchQueryService.cs†L26-L155】【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L23-L229】
- **Řazení a zvýraznění.** `SqliteFts5QueryService` používá `bm25` s váhami 4.0 (titul), 2.0 (autor), 0.8 (`metadata_text`), 0.2 (`metadata`) a 0.1 (MIME). Fragmenty se generují přes funkci `snippet`, přičemž `metadata_text` funguje jako zdroj textu pro zvýraznění i fallback na metadata JSON při kompatibilitě se staršími indexy.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L77-L321】
- **Fuzzy vyhledávání.** Pokud dotaz obsahuje překlep nebo požaduje volnější shodu, trigramový index vrací kandidáty podle Jaccardovy podobnosti. Výsledky se následně kombinují s FTS skóre, aby se zachovalo konzistentní pořadí.【F:Veriado.Application/Search/TrigramQueryBuilder.cs†L79-L156】【F:Veriado.Infrastructure/Search/HybridSearchQueryService.cs†L84-L155】

## Údržba a kvalita indexu
- **Suggestion a spelling.** `SuggestionMaintenanceService` během indexace sklízí tokeny pro tabulku `suggestions`, která napájí autocomplete i kontrolu pravopisu. Stejná data slouží jako slovník pro trigramový spellchecker.【F:Veriado.Infrastructure/Search/SuggestionMaintenanceService.cs†L13-L176】
- **Synonyma.** Tabulka `synonyms` umožňuje mapovat varianty termínů. `SearchQueryBuilder` při kompilaci automaticky rozšíří dotaz o zadané varianty, což zlepšuje vyhledání relevantních dokumentů i při odlišné terminologii.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L170-L321】【F:Veriado.Application/Search/SearchQueryBuilder.cs†L128-L266】
- **Resilience.** Každá operace indexace zachytává chyby `SqliteException`, které indikují poškození nebo chybějící schéma. V takovém případě se vyhodí `SearchIndexCorruptedException`, což umožňuje aplikaci vyvolat proces oprav nebo reindexace.【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L72-L125】
- **Možnosti konfigurace.** Administrátor může upravit počet trigramů, váhy skórování či výchozí jazyk analyzéru v `appsettings.json`. Tím lze vybalancovat výkon mezi přesným a fuzzy vyhledáváním pro konkrétní dataset.【F:Veriado.Application/Search/SearchOptions.cs†L7-L106】

## Doporučení pro další rozvoj
1. **Enrichment metadat.** Doplnění překladů SID → jméno uživatele a lokalizace atributů by zvýšilo kvalitu `metadata_text`, a tím i relevance snippetů.【F:Veriado.Domain/Search/MetadataTextFormatter.cs†L12-L104】
2. **Telemetry indexace.** Sběr metrik (počet tokenů, velikost trigram tabulky, čas indexace) by umožnil sledovat efekt limitu 2 048 trigramů a včas reagovat na anomálie.
3. **Incrementální reindexace.** Aktuální pipeline vyžaduje úplné přepsání řádků. Zavedení fronty pro přírůstkovou aktualizaci trigramů by mohlo zrychlit indexaci velkých dokumentů.
4. **Lepší fallback pro JSON metadata.** Uživatelé využívající staré indexy by ocenili, kdyby se JSON pole transformovalo do čitelnějších párů `key:value` během snippetů, místo syrového JSON.

## Implementovaná zlepšení (2025-10-03)
- **Časový boost relevance.** Konfigurovatelná poločas rozpadu (`SearchOptions.Score.RecencyHalfLifeDays`) aplikuje penalizaci na starší dokumenty přímo v SQL výpočtu ranku. Dotazy tak preferují nově aktualizované soubory, aniž by se změnila logika indexace nebo nutnost reindexace. 【F:Veriado.Application/Search/SearchOptions.cs†L55-L72】【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L341-L374】
