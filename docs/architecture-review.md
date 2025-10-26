# Architektonický přehled Veriado

## Struktura řešení
- **Domain (Veriado.Domain)** – definuje agregáty, hodnotové objekty a doménové události. Například `FileEntity` reprezentuje kompletní životní cyklus souboru včetně verze obsahu, metadat a stavu fulltextového indexu.【F:Veriado.Domain/Files/FileEntity.cs†L8-L149】【F:Veriado.Domain/Files/FileEntity.cs†L474-L501】
- **Application (Veriado.Application)** – obsahuje MediatR handlery, validační pipeline a registraci služeb. `AddApplication` zapojuje pipeline chování (logování, idempotence, validace) a poskytuje ambientní kontext požadavku přes `AmbientRequestContext`.【F:Veriado.Application/DependencyInjection/ApplicationServicesExtensions.cs†L21-L46】【F:Veriado.Application/Abstractions/AmbientRequestContext.cs†L6-L77】
- **Mapping (Veriado.Mapping)** – propojuje DTO vrstvy s příkazy aplikace. `WriteMappingPipeline` kombinuje parsování hodnotových objektů, FluentValidation a staví sadu příkazů pro orchestraci zápisů.【F:Veriado.Mapping/AC/WriteMappingPipeline.cs†L16-L166】
- **Infrastructure (Veriado.Infrastructure)** – zajišťuje přístup k EF Core/SQLite, plnotextové vyhledávání a orchestraci zápisů přímo přes `FileRepository`. Registrace služeb konfiguruje databázové připojení, health-checky a inicializační logiku pro databázi a FTS.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L46-L220】
- **Repositories & persistence** – například `FileRepository` čte agregáty přes `ReadOnlyDbContext` a nyní zapisuje přímo pomocí `AppDbContext` v jedné transakci, přičemž loguje doménové události pro další projekce.【F:Veriado.Infrastructure/Repositories/FileRepository.cs†L16-L220】 `FileEntityConfiguration` mapuje vlastnosti agregátu na tabulku `files` v SQLite včetně vlastněných typů.【F:Veriado.Infrastructure/Persistence/Configurations/FileEntityConfiguration.cs†L6-L157】
- **Services (Veriado.Services)** – vysoká API vrstva pro WinUI, která orchestruje příkazy, validaci a ambientní kontext (např. `FileOperationsService`).【F:Veriado.Services/Files/FileOperationsService.cs†L11-L158】 Importní služba obsluhuje souběžné zpracování souborů a integruje opravy fulltextu.【F:Veriado.Services/Import/ImportService.cs†L320-L518】【F:Veriado.Services/Import/ImportService.cs†L925-L1066】
- **WinUI klient (Veriado.WinUI)** – desktopový host zapouzdřuje DI, inicializuje infrastrukturu a zajišťuje jednorázovou migraci databáze před startem UI.【F:Veriado.WinUI/AppHost.cs†L30-L105】 Sdílený `ISqlitePathResolver` volí umístění databáze a vytváří úložiště při prvním spuštění i pro design-time nástroje.【F:Veriado.Infrastructure/Persistence/Connections/SqlitePathResolver.cs†L12-L81】【F:Veriado.WinUI/AppHost.cs†L68-L104】

## Toky a vazby
1. **Příkazové workflow**: WinUI služba (např. `FileOperationsService`) připraví DTO, využije `WriteMappingPipeline` k validaci/parsing hodnotových objektů a odešle příkaz skrze MediatR, který zpracuje aplikace/handler a přistupuje k repozitáři.【F:Veriado.Services/Files/FileOperationsService.cs†L27-L140】【F:Veriado.Mapping/AC/WriteMappingPipeline.cs†L47-L196】
2. **Zápis dat**: Handlery ukládají agregáty přes `FileRepository`, který nyní otevře transakci nad `AppDbContext`, provede změny a uloží doménové události přímo do tabulky `domain_event_log` – bez asynchronní fronty nebo background workeru.【F:Veriado.Infrastructure/Repositories/FileRepository.cs†L16-L220】
3. **Inicializace infrastruktury**: Při startu UI `AppHost.StartAsync` vypočítá cestu k databázi, zajistí jedinečný mutex pro migrace a volá `InitializeInfrastructureAsync`, který provede EF Core migrace, baseline historie a kontroly konzistence.【F:Veriado.WinUI/AppHost.cs†L66-L105】【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L234-L277】

## Identifikované problémy a chyby
### 1. Chybějící optimistická synchronizace na agregátu souboru
`FileEntity` udržuje verzi (`Version`) a při změně ji zvyšuje, ale EF konfigurace pouze mapuje sloupec bez označení jako souběhový token. Při paralelních zápisech tedy poslední zápis prostě přepíše předchozí stav bez detekce konfliktu.【F:Veriado.Domain/Files/FileEntity.cs†L81-L149】【F:Veriado.Domain/Files/FileEntity.cs†L487-L501】【F:Veriado.Infrastructure/Persistence/Configurations/FileEntityConfiguration.cs†L64-L76】 
*Doporučení*: Nastavit `builder.Property(file => file.Version).IsConcurrencyToken()` nebo použít `RowVersion`/`xmax` ekvivalent a propagovat concurrency chyby až k UI.

### 2. Import načítá celý soubor do paměti a omezuje velikost na 2 GB
`ReadFileContentAsync` otevírá soubor s `FileShare.Read`, alokuje celé pole `byte[]` o velikosti souboru a odmítá větší než `int.MaxValue`. To vytváří vysoké nároky na RAM a znemožní import velmi velkých souborů (např. archivů >2 GB), i když SQLite by mohlo data postupně streamovat do blobu.【F:Veriado.Services/Import/ImportService.cs†L1002-L1066】 
*Doporučení*: Přepsat import na streamování (např. `Stream` -> chunk upload) a delegovat hashování na pipeline bez kopie celé hodnoty.

### 3. Blokující inicializace SQLite během konfigurace DI
V `AddInfrastructureInternal` se volá `SqlitePragmaHelper.ApplyAsync(...).GetAwaiter().GetResult()` a následně synchronně `SqliteFulltextSupportDetector.Detect`, který znovu otevírá připojení a používá `GetAwaiter().GetResult()` na asynchronních pomocnících. Při hostingu v prostředí s omezením synchronního čekání (např. ASP.NET request context) může dojít k deadlocku nebo dlouhému blokování konfiguračního threadu.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L64-L76】【F:Veriado.Infrastructure/Persistence/SqliteFulltextSupportDetector.cs†L19-L47】 
*Doporučení*: Přesunout inicializaci do `IHostedService`/`InitializeInfrastructureAsync`, používat čistě asynchronní kód a odstranit `GetResult` z konfiguračních callbacků.

### 4. Import nelze spustit na současně otevřených souborech
Soubor se otevírá s `FileShare.Read`, takže pokud zdrojový soubor drží jiný proces v režimu `FileShare.ReadWrite` (např. Outlook PST, logy), pokus o import selže s výjimkou. To snižuje robustnost hromadných importů z pracovních složek.【F:Veriado.Services/Import/ImportService.cs†L1002-L1054】 
*Doporučení*: Zvážit `FileShare.ReadWrite` nebo konfigurovatelnou politiku sdílení s detekcí blokovaných souborů.

## Další návrhy na vylepšení
- **Monitoring a telemetrie**: Fulltextová telemetrie (`SearchTelemetry`) poskytuje metriky o dotazech, velikosti indexu a opravách. Do budoucna by stálo za to doplnit ukazatele i pro synchronní zápisy (např. počet uložených agregátů, délku transakcí) a vystavit je přes `Meter` stejně jako stávající metriky fulltextu.【F:Veriado.Infrastructure/Search/SearchTelemetry.cs†L1-L84】
- **Konfigurace infrastruktury**: centrální `ISqlitePathResolver` drží jediný zdroj pravdy pro cestu k databázi a zajišťuje vytvoření úložiště během konfigurace DI i při startu klienta.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L62-L118】【F:Veriado.Infrastructure/Persistence/Connections/SqlitePathResolver.cs†L12-L81】
- **Vylepšená práce s doménovými událostmi**: `FileEntityConfiguration` ignoruje `DomainEvents`. Pokud se mají promítat mimo doménu, je vhodné doplnit outbox pattern (částečně přítomný) i pro další agregáty a zajistit testy pro publikaci událostí.【F:Veriado.Infrastructure/Persistence/Configurations/FileEntityConfiguration.cs†L72-L79】
- **Testovací pokrytí**: integrační testy v `Veriado.Application.Tests` již ověřují inicializaci infrastruktury, ale chybí scénáře pro konflikty zápisů a importy extrémních souborů. Přidání takových testů by odhalilo problémy popsané výše.

## Testování a kvalita
- **Doménové události a konzistence**: integrační scénáře `DomainEventsInterceptorTests` ověřují, že uložení agregátu vyčistí doménové události, zapíše položky do outbox tabulky `domain_event_log`, spouští reindexaci a korektně se vrací do konzistentního stavu při selhání handleru.【F:Veriado.Application.Tests/Infrastructure/DomainEventsInterceptorTests.cs†L50-L153】
- **Fulltextové dotazy**: jednotkové testy `FtsQueryBuilderTests` validují normalizaci tokenů a práci s rezervovanými výrazy, zatímco `SearchFilesHandlerTests` hlídají, že normalizace wildcard segmentů respektuje profil analyzéru a zachovává mezery mezi tokeny.【F:Veriado.Application.Tests/Search/FtsQueryBuilderTests.cs†L11-L70】【F:Veriado.Application.Tests/Search/SearchFilesHandlerTests.cs†L18-L70】

## Nástroje a skripty
- **Benchmark fulltextu**: skript `tools/fts5-benchmark.csx` umožňuje rychle změřit propustnost zápisu do tabulky `search_document` a latence dotazů proti FTS5 indexu včetně aplikace doporučených `PRAGMA` nastavení. Spouští se přes `dotnet script` s cestou k databázi a volitelnými dotazy pro měření p50/p95 latencí.【F:tools/fts5-benchmark.csx†L11-L178】
