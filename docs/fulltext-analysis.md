# Analýza fulltextového vyhledávání

Tento dokument shrnuje aktuální podobu fulltextového vyhledávání po odstranění všech trigramových a fuzzy částí. Vyhledávání běží čistě nad nativními možnostmi SQLite FTS5, což výrazně zjednodušuje nasazení i údržbu.

## Architektura a tok dat
- **FTS5 vrstva.** Schéma SQLite vytváří pouze virtuální tabulku `file_search` doplněnou o mapu `file_search_map`, která převádí `rowid` na primární klíče doménových souborů. Tokenizer `unicode61` pracuje bez diakritiky a používá prázdný `content`, takže aplikace kompletně řídí synchronizaci dat.【F:Veriado.Infrastructure/Persistence/Schema/Fts5.sql†L1-L32】
- **Pipeline indexace.** `FileEntity.ToSearchDocument()` promění doménový agregát na `SearchDocument`. `SqliteFts5Transactional` následně zapíše titulek, MIME, autora, serializovaný souhrn `metadata_text` i JSON `metadata` do jedné transakce. Žádné trigramové tabulky ani hybridní skórování se již neprovádí.【F:Veriado.Domain/Files/FileEntity.cs†L365-L396】【F:Veriado.Domain/Search/SearchDocument.cs†L11-L58】【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L12-L186】
- **Koordinace zápisů.** `SqliteSearchIndexCoordinator` rozhoduje, zda se indexace provede synchronně (`SameTransaction`), nebo se předá do `SqliteFts5Indexer`, který běží na dedikovaném připojení. Obě větve pracují výhradně s FTS5 tabulkami a write-ahead frontou.【F:Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs†L9-L74】【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L15-L122】【F:Veriado.Infrastructure/Search/FtsWriteAheadService.cs†L17-L170】

## Pokrytí indexovaných polí
- **FTS5 sloupce.** `SqliteFts5Transactional` zapisuje do FTS sloupce `title`, `mime`, `author`, `metadata_text` a `metadata`. `metadata_text` slouží pro snippet i vážení, JSON `metadata` drží zpětnou kompatibilitu se staršími indexy. Žádné další pomocné indexy nejsou potřeba.【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L32-L122】
- **Výsledky dotazů.** `SqliteFts5QueryService` provádí `MATCH` dotazy, počítá `bm25` se základními váhami a vrací `SearchHit` s fragmenty ze `metadata_text`. Zvýraznění zajišťuje funkce `snippet` a výsledky se řadí čistě podle BM25 nebo volitelného vlastního skóre.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L21-L229】

## Klíčové služby
- **SearchQueryBuilder.** Generuje FTS5 výrazy, aplikuje synonyma, prefixy, rozsahy a buduje `SearchQueryPlan` bez jakýchkoli fallbacků.【F:Veriado.Application/Search/SearchQueryBuilder.cs†L1-L240】
- **SqliteFts5QueryService.** Zajišťuje kompletní dotazování včetně telemetrie latence bez dalších obálek.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L1-L312】
- **SearchHistoryService / SearchFavoritesService.** Evidují pouze FTS dotazy. Metadata již neobsahují příznak fuzzy hledání a SQL příkazy pracují jen s reálnými sloupci tabulek `search_history` a `search_favorites`.【F:Veriado.Infrastructure/Search/SearchHistoryService.cs†L16-L87】【F:Veriado.Infrastructure/Search/SearchFavoritesService.cs†L15-L118】

## Integrace a konfigurace
- `SearchOptions` poskytují váhy pro BM25, nastavení parseru, facety, synonyma a návrhy. Sekce pro trigramy byla odstraněna, zůstávají pouze relevantní části pro FTS a nápovědu.【F:Veriado.Application/Search/SearchOptions.cs†L5-L76】
- DI registrace v `ServiceCollectionExtensions` vytváří pouze FTS služby: indexer, koordinátor, write-ahead servis, dotazování, historii a oblíbené položky. Neprobíhá žádná registrace trigram komponent.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L56-L151】

## Doporučené kontroly
- Spouštějte `IndexAuditor.VerifyAsync`, aby se detekovaly chybějící záznamy ve `file_search_map`. Audity již neporovnávají trigramové mapy, takže výsledek je přehlednější.【F:Veriado.Infrastructure/Integrity/IndexAuditor.cs†L34-L120】
- Při opravách využijte `FulltextIntegrityService.RepairAsync`, který v případě potřeby dropne a znovu vytvoří pouze FTS5 schéma. Kroky pro trigram tabulky byly odstraněny, což zrychluje rebuildy.【F:Veriado.Infrastructure/Integrity/FulltextIntegrityService.cs†L43-L232】

## Další kroky
- Zaměřte se na kvalitu `metadata_text` – přesnost snippetů nyní přímo ovlivňuje používání výsledků.
- Zvažte přidání integračních testů pokrývajících `SearchQueryBuilder`, `SqliteFts5QueryService` a importní pipeline bez závislosti na trigramových datech.
