# Hardening the Veriado Full-Text Index

Tento dokument shrnuje existující ochranné mechanismy a doporučuje rozšíření, která z Veriada udělají „nerozbitný“ fulltextový index. Cílem je minimalizovat potřebu ručních oprav a zajistit, že FTS5 úložiště zůstane deterministicky reprodukovatelné.

## 1. Opřít se o kanonická data

* **Index je pouze derivát** – vycházejte z toho, že jediným zdrojem pravdy jsou tabulky `files` v relačním jádře. Díky tomu lze index vždy znovu vytvořit z entity `FileEntity` a jejího `SearchIndexState`. 【F:Veriado.Domain/Search/SearchIndexState.cs†L1-L75】
* **Hash obsahu jako strážce** – využívejte `IndexedContentHash` a `IndexedTitle`, abyste v `WriteWorker` jednoznačně věděli, zda jsou data v indexu aktuální. V kombinaci s timestampem `LastIndexedUtc` lze triviálně znovu zjistit, co je potřeba reindexovat. 【F:Veriado.Domain/Search/SearchIndexState.cs†L16-L73】

## 2. Jednotná transakce pro databázi i FTS

* **Sdílené transakce** – běh `WriteWorker` zapisuje entity i index ve stejné transakci (`ApplyFulltextUpdatesAsync` používá `SqliteFts5Transactional`). Pokud transakce spadne, neproběhne ani zápis do indexu. 【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L180-L334】【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L20-L120】
* **Idempotentní operace** – `SqliteFts5Transactional` nejdříve vloží `delete` příkaz, a teprve pak `INSERT`, což zajišťuje, že předchozí segmenty se odstraní i při opakovaných zápisech. Přístup `INSERT OR IGNORE` v mapovacích tabulkách pak zabraňuje duplikátům rowid. 【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L32-L120】

## 3. Automatické zotavení při běhu

* **Detekce korupce** – všechny FTS operace zachytávají `SqliteException` a převádějí ji na `SearchIndexCorruptedException`. Vrstvy nad nimi tak mohou reagovat jednotně. 【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L102-L118】
* **Samozotavení** – `WriteWorker` při zachycení korupce volá `FulltextIntegrityService.RepairAsync` s plným rebuildem a následně batch zopakuje. Díky tomu se běžné chyby opraví bez zásahu uživatele. 【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L140-L236】【F:Veriado.Infrastructure/Integrity/FulltextIntegrityService.cs†L1-L210】

## 4. Startovací a periodické kontroly

* **Kontrola při startu** – `StartupIntegrityCheck` ověřuje integritu tabulek a v logu vypíše chybějící či osiřelé záznamy. Přidejte plánované spouštění této služby například přes `IHostedService`. 【F:Veriado.Infrastructure/Integrity/StartupIntegrityCheck.cs†L12-L60】
* **Telemetrie indexu** – `DiagnosticsRepository` a `SearchTelemetry` už dnes sledují velikost indexu. Doporučuje se přidat metriky pro počet neaktuálních záznamů a výsledky integritních kontrol, aby bylo možné nastavit alerting. 【F:Veriado.Infrastructure/Repositories/DiagnosticsRepository.cs†L60-L85】【F:Veriado.Infrastructure/Search/SearchTelemetry.cs†L12-L55】

## 5. Doporučená rozšíření

1. **Write-ahead žurnál pro index** – při `ApplyFulltextUpdatesAsync` uložte popis operací do pomocné tabulky (např. `fts_write_ahead`). Po potvrzení transakce tabulku vyčistěte. Pokud aplikace spadne uprostřed zápisu, při dalším startu lze žurnál přehrát nebo vyvolat cílený reindex.
2. **Pravidelný end-to-end audit** – naplánujte background job, který 1× denně zavolá `VerifyAsync`. Pokud se objeví neshoda, spustí se automatický reindex pouze chybějících dokumentů, nikoli celého úložiště.
3. **Validace analyzovaných tokenů** – uložte do `SearchIndexState` hash generovaného trigramového řetězce. Při indexaci pak můžete rychle zkontrolovat, jestli došlo ke změně konfigurace analyzeru nebo `TrigramIndexOptions` a případně vyvolat reindex.
4. **Oddělený proces pro rebuild** – u rozsáhlých instalací je vhodné rebuild spouštět mimo hlavní proces (např. CLI nástroj), aby se zabránilo vyčerpání zdrojů produkční instance.
5. **Sledování sqlite pragmat** – pravidelně logujte nastavení `page_size`, `journal_mode` a `foreign_keys`. Nekonzistentní konfigurace může být prvotní příčinou korupce indexu; jejich automatické vynucení přes `SqlitePragmaHelper.ApplyAsync` je dobré ještě doplnit o health check.

Dodržováním výše uvedených principů získáte index, který je deterministicky obnovitelný, průběžně monitorovaný a dokáže se sám zotavit z většiny běžných selhání bez nutnosti ručních oprav.
