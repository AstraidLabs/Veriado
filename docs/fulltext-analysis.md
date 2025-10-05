# Analýza fulltextového vyhledávání

## Architektura a tok dat
- **Lucene index.** Indexace dokumentů probíhá výhradně přes `LuceneSearchIndexer`, který převádí `SearchDocument` na Lucene.NET dokument a zapisuje jej do lokálního adresáře. Stejný objekt aktualizuje i tabulku návrhů pro autocomplete.【F:Veriado.Infrastructure/Search/LuceneSearchIndexer.cs†L1-L42】
- **Příprava dokumentu.** `FileEntity.ToSearchDocument()` vytváří normalizovaný model včetně JSON metadat, lidsky čitelného souhrnu (`MetadataText`) a hashů pro detekci změn.【F:Veriado.Domain/Files/FileEntity.cs†L365-L401】【F:Veriado.Domain/Search/SearchDocument.cs†L11-L58】
- **Koordinace zápisů.** `SearchIndexCoordinator` rozhoduje, zda se indexace provede ihned v rámci write workeru, nebo se úloha uloží do outboxu k pozdějšímu zpracování. Není potřeba sdílet SQLite transakce – Lucene běží v samostatném souborovém úložišti.【F:Veriado.Infrastructure/Search/SearchIndexCoordinator.cs†L1-L52】
- **Outbox typy.** `WriteWorker` přidává do tabulky `outbox_events` jak požadavky na reindexaci, tak odstranění dokumentů. `OutboxDrainService` následně rozlišuje typy událostí a spouští odpovídající operaci nad Lucene indexem.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L320-L392】【F:Veriado.Infrastructure/Search/Outbox/OutboxDrainService.cs†L116-L263】

## Dotazy a skórování
- **Lucene vyhledávání.** `LuceneSearchQueryService` překládá plán dotazu na Lucene `Query` a vrací `SearchHit` včetně snippetů a normalizovaného skóre. Stejná služba se používá pro přesné i fuzzy dotazy; fuzzy fallback se liší pouze vstupními tokeny.【F:Veriado.Infrastructure/Search/LuceneSearchQueryService.cs†L1-L238】
- **Sestavení dotazu.** `SearchQueryBuilder` nadále vytváří `SearchQueryPlan`, ale výsledný `MatchExpression` se interpretuje jako Lucene dotaz (např. `title:"daňový doklad" AND mime:application/pdf`). Raw text dotazu se předává v `RawQueryText`, takže je zachováno logování a historie vyhledávání.【F:Veriado.Application/Search/SearchQueryBuilder.cs†L319-L372】【F:Veriado.Application/Search/SearchQueryPlan.cs†L1-L46】
- **Trigramová heuristika.** `TrigramQueryBuilder` generuje množiny trigramů pro fuzzy scénáře. Při nedostatku shod nebo prefixových dotazech může orchestrátor použít trigramový výraz a výsledky následně normalizovat podle skóre z Lucene.【F:Veriado.Application/Search/TrigramQueryBuilder.cs†L1-L115】

## Facety, návrhy a kontrola pravopisu
- `FacetService`, `SuggestionService` a `SpellSuggestionService` nadále využívají relační databázi a tabulku `suggestions`. `SuggestionMaintenanceService` sbírá tokeny při indexaci a ukládá je s váhami pro autocomplete i slovník pro trigramový spellchecker.【F:Veriado.Infrastructure/Search/SuggestionMaintenanceService.cs†L1-L101】

## Integrita a obnova
- **Ověření konzistence.** `FulltextIntegrityService` porovnává ID dokumentů v Lucene s tabulkou `files`. Při neshodě smaže osiřelé položky a znovu indexuje chybějící soubory pomocí `ISearchIndexer`. Celý proces lze spustit ručně nebo automaticky při zjištěné chybě.【F:Veriado.Infrastructure/Integrity/FulltextIntegrityService.cs†L1-L129】
- **WriteWorker** sleduje stav `SearchIndexState` na agregátu a volá `SearchIndexCoordinator`. V případě chyby požádá integritní službu o plný rebuild a batch zopakuje.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L180-L421】

## Konfigurace
Konfigurace vyhledávání se provádí přes sekci `Search` v `appsettings.json`. Kromě vážení (`Score`) a tokenizace (`Analyzer`) lze nastavit `SearchIndexingMode` v `InfrastructureOptions`, který určuje zda se zápisy provádí okamžitě (`Immediate`) nebo přes outbox (`Outbox`).【F:Veriado.Infrastructure/Persistence/Options/InfrastructureOptions.cs†L23-L62】【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L121-L169】

Tato architektura eliminuje závislost na SQLite FTS5 a poskytuje plně souborový Lucene index, který je snadno přenositelný a deterministicky znovu vytvořitelný.
