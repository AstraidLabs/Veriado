# Analýza indexace vyhledávání

Tento dokument shrnuje, jak Veriado indexuje soubory do Lucene a jaké podpůrné komponenty se starají o konzistenci a fuzzy hledání.

1. **Transformace agregátu.** `FileEntity.ToSearchDocument()` vytváří `SearchDocument` s názvem, MIME typem, autorem, názvem souboru, textovým shrnutím (`MetadataText`) a JSON metadaty. Součástí je také hash obsahu a časové údaje pro řazení výsledků.【F:Veriado.Domain/Files/FileEntity.cs†L365-L401】【F:Veriado.Domain/Search/SearchDocument.cs†L11-L58】
2. **Zápis do Lucene.** `LuceneSearchIndexer` převádí dokument na Lucene `Document` a ukládá jej pomocí `IndexWriter.UpdateDocument`. Zároveň aktualizuje tabulku `suggestions`, aby byly k dispozici autocomplete a spell-check data.【F:Veriado.Infrastructure/Search/LuceneSearchIndexer.cs†L1-L42】【F:Veriado.Infrastructure/Search/SuggestionMaintenanceService.cs†L17-L83】
3. **Koordinace indexace.** `SearchIndexCoordinator` vyhodnotí `SearchIndexingMode`. Pokud je nastaven `Outbox` a soubor dovoluje odklad, vrátí `false` a zápis se provede až při drainu outboxu. V opačném případě zavolá indexer přímo.【F:Veriado.Infrastructure/Search/SearchIndexCoordinator.cs†L1-L52】
4. **Outbox workflow.** `OutboxDrainService` načítá nevyřízené události, načte odpovídající `FileEntity` a provede reindexaci přes `ISearchIndexer`. Po úspěchu aktualizuje `SearchIndexState` a nastaví `ProcessedUtc`. Služba má retry logiku i reakci na detekovanou nekonzistenci indexu.【F:Veriado.Infrastructure/Search/Outbox/OutboxDrainService.cs†L32-L188】
5. **Write worker.** `WriteWorker` zpracovává dávky zápisů, sleduje zda se soubory změnily, a podle výsledku volá `SearchIndexCoordinator`. Po úspěšném zápisu aktualizuje `SearchIndexState` podpisem z `SearchIndexSignatureCalculator`. Při chybě spouští integritní opravu a dávku opakuje.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L200-L421】
6. **Dotazování.** `LuceneSearchQueryService` využívá `SearchQueryPlan` a `LuceneIndexManager` k provedení dotazu. Výsledky obsahují normalizované skóre, snippet z metadat a další pole potřebná pro UI.【F:Veriado.Infrastructure/Search/LuceneSearchQueryService.cs†L1-L238】

Díky této architektuře lze index kdykoli kompletně obnovit z databáze a zároveň je možné škálovat zápisy pomocí outboxu, pokud je potřeba oddělit I/O Lucene od hlavní transakce.
