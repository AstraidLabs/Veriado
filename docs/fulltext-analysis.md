# Analýza fulltextového vyhledávání

Tento dokument shrnuje aktuální podobu fulltextového vyhledávání po odstranění všech trigramových a fuzzy částí. Vyhledávání běží čistě nad nativními možnostmi SQLite FTS5, což výrazně zjednodušuje nasazení i údržbu.

- **FTS5 vrstva.** Schéma SQLite vytváří content-linked virtuální tabulku `file_search`, která odebírá data z `DocumentContent`. Tokenizer `unicode61` pracuje bez diakritiky a triggery zajišťují synchronizaci po každém INSERT/UPDATE/DELETE na `DocumentContent`.【F:Veriado.Infrastructure/Persistence/Schema/Fts5.sql†L1-L36】
- **Pipeline indexace.** `FileEntity.ToSearchDocument()` promění doménový agregát na `SearchDocument`. `SqliteFts5Transactional` následně upsertuje záznam v `DocumentContent`; triggery garantují odstranění staré verze a zápis nového obsahu do FTS bez trigramových tabulek.【F:Veriado.Domain/Files/FileEntity.cs†L365-L416】【F:Veriado.Domain/Search/SearchDocument.cs†L11-L58】【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L12-L154】
- **Koordinace zápisů.** `SqliteSearchIndexCoordinator` rozhoduje, zda se indexace provede synchronně (`SameTransaction`), nebo se předá do `SqliteFts5Indexer`, který běží na dedikovaném připojení. Obě větve pracují výhradně s FTS5 tabulkami a write-ahead frontou.【F:Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs†L9-L74】【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L15-L122】【F:Veriado.Infrastructure/Search/FtsWriteAheadService.cs†L17-L170】

## Pokrytí indexovaných polí
- **FTS5 sloupce.** `DocumentContent` uchovává `Title`, `Author`, `Mime`, `MetadataText` a `Metadata`; FTS tabulka `file_search` tato pole automaticky přebírá a používá pro snippety i vážení. Žádné další pomocné indexy nejsou potřeba.【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L32-L120】【F:Veriado.Infrastructure/Persistence/Schema/Fts5.sql†L1-L36】
- **Výsledky dotazů.** `SqliteFts5QueryService` provádí `MATCH` dotazy, počítá `bm25` s váhami odpovídajícími pořadí content-linked sloupců a vrací `SearchHit` s fragmenty ze `metadata_text`. Zvýraznění zajišťuje funkce `snippet` a výsledky se řadí čistě podle BM25 nebo volitelného vlastního skóre.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L21-L336】

## Klíčové služby
- **SearchQueryBuilder.** Generuje FTS5 výrazy, aplikuje synonyma, prefixy, rozsahy a buduje `SearchQueryPlan` bez jakýchkoli fallbacků.【F:Veriado.Application/Search/SearchQueryBuilder.cs†L1-L240】
- **SqliteFts5QueryService.** Zajišťuje kompletní dotazování včetně telemetrie latence bez dalších obálek.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L1-L312】
- **SearchHistoryService / SearchFavoritesService.** Evidují pouze FTS dotazy. Metadata již neobsahují příznak fuzzy hledání a SQL příkazy pracují jen s reálnými sloupci tabulek `search_history` a `search_favorites`.【F:Veriado.Infrastructure/Search/SearchHistoryService.cs†L16-L87】【F:Veriado.Infrastructure/Search/SearchFavoritesService.cs†L15-L118】

## Integrace a konfigurace
- `SearchOptions` poskytují váhy pro BM25, nastavení parseru, facety, synonyma a návrhy. Sekce pro trigramy byla odstraněna, zůstávají pouze relevantní části pro FTS a nápovědu.【F:Veriado.Application/Search/SearchOptions.cs†L5-L76】
- DI registrace v `ServiceCollectionExtensions` vytváří pouze FTS služby: indexer, koordinátor, write-ahead servis, dotazování, historii a oblíbené položky. Neprobíhá žádná registrace trigram komponent a nově se spouští periodický audit jako hosted service.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L56-L214】

## Doporučené kontroly
- Spouštějte `IndexAuditor.VerifyAsync`, aby se detekovaly chybějící záznamy v `DocumentContent`. Audity probíhají nad jednou tabulkou a nezahrnují žádná trigramová metadata.【F:Veriado.Infrastructure/Integrity/IndexAuditor.cs†L1-L212】
- Při opravách využijte `FulltextIntegrityService.RepairAsync`, který v případě potřeby dropne a znovu vytvoří pouze FTS5 schéma. Kroky pro trigram tabulky byly odstraněny, což zrychluje rebuildy.【F:Veriado.Infrastructure/Integrity/FulltextIntegrityService.cs†L43-L232】

## Další kroky
- Zaměřte se na kvalitu `metadata_text` – přesnost snippetů nyní přímo ovlivňuje používání výsledků.
- Zvažte přidání integračních testů pokrývajících `SearchQueryBuilder`, `SqliteFts5QueryService` a importní pipeline bez závislosti na trigramových datech.
