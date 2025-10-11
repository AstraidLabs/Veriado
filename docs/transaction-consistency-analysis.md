# Přehled transakční logiky a konektivity

Tento dokument shrnuje, jak je ve Veriado řešená koordinace Entity Framework Core kontextu, SQLite připojení a FTS5 indexu. Cílem je předejít rozkolu mezi operacemi běžícími uvnitř transakce a mimo ni a lépe pochopit ochranné mechanismy proti chybám v konektivitě.

## 1. Primární zápisová pipeline (`WriteWorker`)

- `WriteWorker.ProcessBatchAttemptAsync` vždy vytváří nový `AppDbContext`, otevírá databázovou transakci a zpracuje šarži požadavků nad sdílenou transakcí EF Core. Selhání jakéhokoliv požadavku vede ke zrušení celé transakce a propagaci chyby všem volajícím.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L248-L314】【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L330-L375】
- Po úspěšném zapsání změn volá `ApplyFulltextUpdatesAsync`, aby synchronizoval stav FTS indexu v rámci téže transakce (pokud to konfigurace dovolí) a až poté transakci potvrzuje. Tím se zaručuje atomické chování mezi relačními tabulkami a fulltext indexem.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L379-L469】
- `ExecuteWithRetryAsync` přidává opakování u krátkodobých chyb, čímž snižuje riziko transientních selhání připojení nebo zámků v SQLite.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L497-L541】

## 2. Indexace ve stejné transakci

- `ApplyFulltextUpdatesAsync` vyžaduje `SqliteTransaction` z aktuálního `AppDbContextu`. Pokud jej nedokáže získat, zpracování šarže se okamžitě ukončí výjimkou, aby se předešlo mixu různých providerů nebo nepřipojených transakcí.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L364-L466】
- `SqliteSearchIndexCoordinator` odmítne jakoukoli transakci, která není typu `SqliteTransaction`, a tím vynutí, že fulltextová operace poběží na stejném připojení jako EF Core context.【F:Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs†L37-L64】
- `SqliteFts5Transactional` provádí mazání i zápis do FTS tabulek uvnitř sdílené transakce, přičemž využívá žurnál `fts_write_ahead` a mapuje korupci na `SearchIndexCorruptedException`. Tím dává `WriteWorkeru` prostor spustit automatickou opravu a zkusit šarži zpracovat znovu.【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L23-L188】
- `PooledSqliteConnectionFactory` sleduje generace připojení. Po resetu (např. po opravě indexu) zneplatní staré generace, takže žádná služba nepracuje se zastaralým WAL nebo schématem.【F:Veriado.Infrastructure/Search/ISqliteConnectionFactory.cs†L43-L134】

## 3. Ochrana proti chybám konektivity

- Každá FTS operace prochází žurnálem (`FtsWriteAheadService`), který umožňuje opětovné přehrání nedokončených kroků. Záznam se maže až po úspěšném insert/delete v indexu a případném callbacku `beforeCommit`, takže se neztratí ani při výpadku připojení.【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L36-L82】【F:Veriado.Infrastructure/Search/FtsWriteAheadService.cs†L110-L210】
- `ExecuteWithRetryAsync` minimalizuje dopad dočasných chyb (např. „database is locked“ nebo `SQLITE_IOERR`). Po vyčerpání pokusů je výjimka bublána ven, což udržuje konzistentní stav – buď transakce projde, nebo se celá vrátí zpět.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L483-L548】
- `WriteWorker` zachytává `SearchIndexCorruptedException` a spouští automatickou opravu (`AttemptIntegrityRepairAsync`). Teprve pokud oprava selže, je chyba eskalována dál, takže nehrozí trvalý rozkol mezi transakcemi EF a FTS stavem.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L275-L336】【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L550-L586】

## 4. Doporučení pro hladký provoz

1. **Ověření providera** – `InitializeInfrastructureAsync` fail-fast ověřuje, že aplikace běží na `Microsoft.Data.Sqlite`. Pokud dojde k nechtěné změně providera, inicializace skončí výjimkou ještě před spuštěním workerů.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L210-L240】
2. **Monitoring retry logů** – varovné logy z `ExecuteWithRetryAsync` signalizují transientní problémy. Pokud se jejich počet zvyšuje, zvažte úpravu `BatchWindowMs` nebo velikosti poolu připojení.
3. **Pravidelný reset poolu po údržbě** – po ručních zásazích do FTS tabulek zavolejte `ISqliteConnectionFactory.ResetAsync`, aby žádné připojení nezůstalo na starém schématu.
4. **Zdravotní kontrola žurnálu** – sledujte velikost tabulky `fts_journal`. Přerůstající žurnál může indikovat, že některé `ClearAsync` volání nedobíhají kvůli výpadkům.

Současná architektura zajišťuje, že všechny zápisy i fulltextové operace probíhají nad totožným připojením a transakcí. Dodržováním výše uvedených doporučení lze předejít většině chyb konektivity i rozkolu stavu mezi databází a fulltextovým indexem.
