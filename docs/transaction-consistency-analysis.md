# Přehled transakční logiky a konektivity

Tento dokument shrnuje, jak je ve Veriado řešená koordinace Entity Framework Core kontextu, SQLite připojení a FTS5 indexu. Cílem je předejít rozkolu mezi operacemi běžícími uvnitř transakce a mimo ni a lépe pochopit ochranné mechanismy proti chybám v konektivitě.

## 1. Primární zápisová pipeline (`WriteWorker`)

- `WriteWorker.ProcessBatchAttemptAsync` vždy vytváří nový `AppDbContext`, otevírá databázovou transakci a zpracuje šarži požadavků nad sdílenou transakcí EF Core. Selhání jakéhokoliv požadavku vede ke zrušení celé transakce a propagaci chyby všem volajícím.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L248-L314】【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L330-L375】
- Po úspěšném zapsání změn volá `ApplyFulltextUpdatesAsync`, aby synchronizoval stav FTS indexu v rámci téže transakce (pokud to konfigurace dovolí) a až poté transakci potvrzuje. Tím se zaručuje atomické chování mezi relačními tabulkami a fulltext indexem.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L379-L469】
- `ExecuteWithRetryAsync` přidává opakování u krátkodobých chyb, čímž snižuje riziko transientních selhání připojení nebo zámků v SQLite.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L497-L541】

## 2. Indexace ve stejné transakci (`FtsIndexingMode.SameTransaction`)

- Režim „SameTransaction“ je aktivní, pokud je FTS dostupný a `FilePersistenceOptions` zakazuje odložení indexace. V takovém případě `ApplyFulltextUpdatesAsync` očekává, že `DbTransaction` je typu `SqliteTransaction`, a jinak vyhodí výjimku – to brání použití jiného poskytovatele nebo nepřipojené transakce.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L420-L434】
- Třída `SqliteSearchIndexCoordinator` hlídá, aby se do helperu nedostal jiný typ transakce, a vynucuje okamžitou indexaci nad existujícím připojením EF Core. Pokud transakce chybí, režim `SameTransaction` je výslovně zablokován výjimkou, což brání silentnímu pádu na nesdíleném připojení.【F:Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs†L37-L71】
- `SqliteFts5Transactional` reálně provádí delete/insert nad FTS tabulkami, pracuje se sdíleným `SqliteConnection`, zapisuje žurnál do `fts_journal` tabulky a případně žurnál promazává před potvrzením transakce. Přitom převádí `SqliteException` indikující korupci na aplikační výjimku, aby `WriteWorker` mohl spustit opravný proces.【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L23-L109】【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L117-L188】

## 3. Odložená indexace (`FtsIndexingMode.Outbox`)

- Pokud konfigurace dovoluje odloženou indexaci (`AllowDeferredIndexing == true`), `WriteWorker` publikuje outbox událost místo okamžité indexace. Tím se odděluje kritická transakce EF Core od případných výkyvů v FTS vrstvě.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L403-L422】
- `SqliteFts5Indexer` obsluhuje deferred scénář: pomocí `ISqliteConnectionFactory` si půjčí nové připojení, otevře transakci čistě pro FTS operaci a po úspěchu ji potvrdí. Při výjimce dojde k rollbacku a připojení se vrací do poolu v uzavřeném stavu, aby se předešlo „rozpojeným“ stavům.【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L42-L109】
- `PooledSqliteConnectionFactory` hlídá generace připojení. Pokud dojde k resetu (např. po opravě indexu), starší generace se při vrácení do poolu zahodí, takže žádné připojení nepracuje s již neplatným schématem nebo WAL souborem.【F:Veriado.Infrastructure/Search/ISqliteConnectionFactory.cs†L43-L134】

## 4. Ochrana proti chybám konektivity

- Každá FTS operace prochází žurnálem (`FtsWriteAheadService`), který umožňuje opětovné přehrání nedokončených kroků. Záznam se maže až po úspěšném insert/delete v indexu a případném callbacku `beforeCommit`, takže se neztratí ani při výpadku připojení.【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L36-L82】【F:Veriado.Infrastructure/Search/FtsWriteAheadService.cs†L110-L210】
- `ExecuteWithRetryAsync` minimalizuje dopad dočasných chyb (např. „database is locked“). Po vyčerpání pokusů je výjimka bublána ven, což udržuje konzistentní stav – buď transakce projde, nebo se celá vrátí zpět.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L497-L541】
- `WriteWorker` zachytává `SearchIndexCorruptedException` a spouští automatickou opravu (`AttemptIntegrityRepairAsync`). Teprve pokud oprava selže, je chyba eskalována dál, takže nehrozí trvalý rozkol mezi transakcemi EF a FTS stavem.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L282-L318】【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L543-L566】

## 5. Doporučení pro hladký provoz

1. **Hlídání konfigurace** – ujistěte se, že nasazení s režimem `SameTransaction` opravdu běží proti SQLite. Pokud se přešlo na jiný DB provider, nastavte `FtsIndexingMode.Outbox`, jinak `SqliteSearchIndexCoordinator` záměrně vyhodí chybu.
2. **Monitoring retry logů** – varovné logy z `ExecuteWithRetryAsync` signalizují transientní problémy. Pokud se jejich počet zvyšuje, zvažte úpravu `BatchWindowMs` nebo velikosti poolu připojení.
3. **Pravidelný reset poolu po údržbě** – po ručních zásazích do FTS tabulek zavolejte `ISqliteConnectionFactory.ResetAsync`, aby žádné připojení nezůstalo na starém schématu.
4. **Zdravotní kontrola žurnálu** – sledujte velikost tabulky `fts_journal`. Přerůstající žurnál může indikovat, že některé `ClearAsync` volání nedobíhají kvůli výpadkům.
5. **Testování mimo transakci** – pokud píšete nové služby, které volají `ISearchIndexCoordinator` bez ambientní transakce, vždy nasaďte režim Outbox nebo si explicitně otevřete `SqliteTransaction`, abyste zachovali konzistenci.

Tato architektura už dnes zajišťuje, že nedochází k mixu „připojených“ a „nepřipojených“ operací – buď se všechno děje uvnitř jedné EF Core + SQLite transakce, nebo se indexace oddělí do samostatné transakce s vlastním připojením. Dodržováním výše uvedených doporučení lze předejít většině chyb konektivity i rozkolu stavu mezi databází a fulltextovým indexem.
