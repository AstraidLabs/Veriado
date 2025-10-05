# Hardening the Veriado Full-Text Index

Tento dokument shrnuje mechanizmy, které chrání Lucene index před nekonzistencí, a doporučuje další kroky pro produkční nasazení.

## 1. Opřít se o kanonická data

* **Index je derivát.** Jediným zdrojem pravdy zůstává relační model (`files`, `file_contents`, `search_index_state`). Díky `SearchIndexState` lze snadno detekovat změny a rozhodnout, které soubory znovu indexovat.【F:Veriado.Domain/Search/SearchIndexState.cs†L1-L75】
* **Hash a normalizovaný titul.** `SearchIndexSignatureCalculator` generuje hash kombinující analyzér, trigramy a normalizovaný název souboru. Write worker jej ukládá po úspěšné indexaci, čímž zamezí zbytečným reindexům.【F:Veriado.Infrastructure/Search/SearchIndexSignatureCalculator.cs†L1-L120】

## 2. Bezpečný zápis

* **Oddělené úložiště.** Lucene zapisuje do vlastního adresáře, takže SQLite transakce nejsou sdílené. Write worker však stále zpracovává data v dávkách, a dokud `ConfirmIndexed` neproběhne, zůstává položka označená jako „stale“.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L300-L410】
* **Deterministické přepsání.** `LuceneSearchIndexer` používá `IndexWriter.UpdateDocument`, který atomicky nahradí předchozí dokument daného GUID. V případě pádu nehrozí duplikace ani částečně zapsané záznamy.【F:Veriado.Infrastructure/Search/LuceneSearchIndexer.cs†L15-L38】

## 3. Automatické zotavení

* **Záchyt chyb.** Pokud během indexace dojde k výjimce, write worker označí požadavek za neúspěšný a nechá jej znovu zpracovat. Při opakované chybě se volá `FulltextIntegrityService.RepairAsync`, který Lucene index znovu vybuduje z relační databáze.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L200-L248】【F:Veriado.Infrastructure/Integrity/FulltextIntegrityService.cs†L53-L129】
* **Outbox jako pojistka.** Při nastavení `SearchIndexingMode.Outbox` se požadavky na reindex vloží do tabulky `outbox_events`. `OutboxDrainService` je zpracuje mimo hlavní transakci a v případě chyb má vlastní retry logiku.【F:Veriado.Infrastructure/Search/Outbox/OutboxDrainService.cs†L1-L188】

## 4. Startovací a periodické kontroly

* **StartupIntegrityCheck.** Při startu procesu lze spustit integritní kontrolu, která zjistí chybějící nebo osiřelé položky a případně je rovnou opraví. Služba používá stejnou implementaci jako ruční `VerifyAndRepairFulltext` use-case.【F:Veriado.Infrastructure/Integrity/StartupIntegrityCheck.cs†L12-L54】【F:Veriado.Application/UseCases/Maintenance/VerifyAndRepairFulltextHandler.cs†L6-L42】
* **Telemetrie.** `SearchTelemetry` sleduje latence dotazů, zatímco `DiagnosticsRepository` poskytuje statistiky o velikosti indexu. Doporučuje se doplnit alerting na růst počtu „stale“ dokumentů a čas poslední úspěšné integritní kontroly.【F:Veriado.Infrastructure/Search/SearchTelemetry.cs†L1-L60】【F:Veriado.Infrastructure/Repositories/DiagnosticsRepository.cs†L54-L112】

## 5. Doporučená rozšíření

1. **Snapshoty indexu.** Pravidelně vytvářejte archiv adresáře s indexem (např. pomocí `IndexWriter.CreateSnapshot`). Umožní to rychlou obnovu bez potřeby kompletního reindexu při havárii disku.
2. **Kontrola verzí analyzéru.** Pokud změníte konfiguraci analyzéru (`AnalyzerOptions`), incrementujte verzi v `SearchIndexState`, aby se automaticky spustil reindex všech dokumentů.
3. **Monitoring volného místa.** Lucene ukládá index do FS; sledujte dostupné místo a nastavte alerty na kritické hodnoty.
4. **Audit outboxu.** U režimu `Outbox` přidejte metriky počtu nevyřízených událostí. Dlouhodobě vysoké hodnoty mohou signalizovat blokovanou indexaci.
5. **Pravidelné spuštění `VerifyAsync`.** Naplánujte background job, který 1× denně ověří konzistenci a případně spustí cílený reindex jen chybějících dokumentů.

Dodržováním těchto principů získáte Lucene index, který je deterministicky obnovitelný, monitorovaný a odolný proti běžným provozním chybám.
