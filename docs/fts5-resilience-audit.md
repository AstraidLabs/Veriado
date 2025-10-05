# Veriado Lucene Resilience Audit

- Lucene indexing je integrováno s write workerem – každý zápis aktualizuje relační model a následně volá `ISearchIndexer`. Pokud dojde k chybě, worker dávku přeruší a vyvolá plný rebuild přes `IFulltextIntegrityService`.
- Integritní služba dokáže zrekonstruovat index čistě z tabulky `files`. Porovnává ID uložená v Lucene s databází, maže osiřelé dokumenty a reindexuje chybějící položky.【F:Veriado.Infrastructure/Integrity/FulltextIntegrityService.cs†L53-L129】
- Outbox mód poskytuje další stupeň odolnosti – indexační požadavky se ukládají do `outbox_events` a `OutboxDrainService` je zpracovává s vlastní retry strategií. Případné výjimky neblokují write pipeline a lze je zpracovat mimo špičku.【F:Veriado.Infrastructure/Search/Outbox/OutboxDrainService.cs†L32-L188】
- `LuceneIndexManager` atomicky nahrazuje dokumenty pomocí `IndexWriter.UpdateDocument` a používá `SemaphoreSlim`, aby zabránil paralelním kolizím. Všechny operace probíhají na sdíleném writeru a po každé mutaci se volá `Commit()`.【F:Veriado.Infrastructure/Search/LuceneIndexManager.cs†L20-L116】
- Telemetrie (`SearchTelemetry`) sleduje latence vyhledávání a zápisů, což umožňuje nastavit alerty na případné degradace výkonu nebo hromadění outbox událostí.【F:Veriado.Infrastructure/Search/SearchTelemetry.cs†L1-L60】

## Doporučení
1. Aktivujte pravidelné spouštění `IFulltextIntegrityService.VerifyAsync` (např. 1× denně) a výsledek logujte pro audit.
2. Sledujte velikost Lucene adresáře a porovnávejte ji s počtem dokumentů – nečekané rozdíly mohou indikovat problém.
3. Zálohujte adresář s indexem po větších dávkách importů, abyste mohli provést rychlý rollback bez nutnosti kompletního reindexu.
