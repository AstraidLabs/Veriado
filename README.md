# Veriado

Veriado je desktopová aplikace pro katalogizaci dokumentů s plnotextovým vyhledáváním. WinUI klient běží nad aplikační vrstvou postavenou na MediatR, ukládá metadata do SQLite a udržuje FTS5 index. Tento přehled shrnuje hlavní schopnosti řešení, architekturu a kroky potřebné pro lokální spuštění.

## Klíčové funkce

### Správa katalogu souborů
- Agregát `FileEntity` uchovává kompletní metadata dokumentu, vazbu na binární obsah a stav fulltextového indexu včetně platnosti dokumentu a systémových metadat.【F:Veriado.Domain/Files/FileEntity.cs†L10-L198】
- `FileOperationsService` mapuje volání z UI na validační pipeline a MediatR příkazy – podporuje přejmenování, úpravy metadat, nastavení režimu jen pro čtení, platnosti dokumentu i aplikaci systémových metadat z NTFS.【F:Veriado.Services/Files/FileOperationsService.cs†L11-L158】
- `FileQueryService` zprostředkuje stránkované gridy, detail souboru i práci s historií/oblíbenými dotazy tak, aby UI nemuselo přistupovat přímo k handlerům.【F:Veriado.Services/Files/FileQueryService.cs†L7-L68】

### Fulltextové vyhledávání
- `SearchFacade` sestavuje FTS5 dotazy, provádí je nad indexem a ukládá historii i oblíbené filtry pro opětovné použití.【F:Veriado.Services/Search/SearchFacade.cs†L9-L99】
- Infrastruktura registruje služby pro analyzér tokenů, historii hledání, oblíbené dotazy i údržbu indexu při startu hostitele.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L115-L199】

### Import dokumentů
- `ImportPageViewModel` řídí hromadný import složek v UI, sleduje průběh, podporuje filtr chyb, export logu a obnovu posledního nastavení z hot-state úložiště.【F:Veriado.WinUI/ViewModels/Import/ImportPageViewModel.cs†L17-L199】
- `ImportService` provádí paralelní zpracování souborů se sdíleným kanálem průběhu, detekcí duplicit, opravou FTS indexu a přerušitelným během.【F:Veriado.Services/Import/ImportService.cs†L320-L520】

### Údržba a diagnostika
- `MaintenanceService` zapouzdřuje příkazy pro vacuum/optimalizaci databáze, ověření a opravy FTS indexu i reindex po změně schématu.【F:Veriado.Services/Maintenance/MaintenanceService.cs†L5-L39】
- `HealthService` vrací diagnostiku infrastruktury a statistiku indexu, kterou WinUI klient zobrazuje v přehledech stavu.【F:Veriado.Services/Diagnostics/HealthService.cs†L6-L26】

### Hostování WinUI aplikace
- `AppHost` sestaví `IHost`, registruje služby UI, aplikační a infrastrukturní vrstvu, zajistí migraci databáze pod mutexem a inicializuje hot-state po startu.【F:Veriado.WinUI/AppHost.cs†L19-L137】
- `SqlitePathResolver` udržuje jednotné určení cesty k databázi, vytváří úložiště při prvním spuštění a podporuje design-time přepis cesty přes proměnnou prostředí.【F:Veriado.Infrastructure/Persistence/Connections/SqlitePathResolver.cs†L7-L92】

## Struktura řešení
- **Veriado.Domain** – doménové agregáty, hodnotové objekty a doménové události související se soubory a fulltextem.【F:Veriado.Domain/Files/FileEntity.cs†L10-L198】
- **Veriado.Application** – MediatR handlery, validační pipeline a ambientní kontext požadavku; registruje se přes `AddApplication`.
- **Veriado.Infrastructure** – přístup k SQLite/EF Core, FTS5, úložišti souborů a infrastrukturním health-checkům.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L55-L199】
- **Veriado.Mapping** – DTO-to-command mapping s FluentValidation pro zápisové operace.【F:Veriado.Mapping/AC/WriteMappingPipeline.cs†L16-L166】
- **Veriado.Services** – façade vrstva pro WinUI (soubory, import, údržba, diagnostika).【F:Veriado.Services/Files/FileOperationsService.cs†L11-L158】
- **Veriado.WinUI** – desktopový klient (MVVM) postavený na Windows App SDK a CommunityToolkit.
- **Veriado.Application.Tests** – integrační testy ověřující doménové události, reindex a konzistenci perzistence.【F:Veriado.Application.Tests/Infrastructure/DomainEventsInterceptorTests.cs†L19-L154】
- **tools/** – pomocné skripty, např. `fts5-benchmark.csx` pro měření výkonu fulltextu.

## File Detail – architektura & bindingy
- `FilesPageViewModel.OpenDetailCommand` vytváří viewmodel dialogu přes `IDialogService`, načte detail souboru a po potvrzení vyvolá refresh seznamu.【F:Veriado.WinUI/ViewModels/Files/FilesPageViewModel.cs†L600-L636】
- `FileDetailDialogViewModel` implementuje `IDialogAware`, spravuje `EditableFileDetailModel` s DataAnnotations validací a orchestruje uložení přes aplikační `IFileService` včetně zobrazení chyb a konfliktů.【F:Veriado.WinUI/ViewModels/Files/FileDetailDialogViewModel.cs†L14-L221】
- `EditableFileDetailModel` dedikuje validaci a mapování na DTO, řeší datovou konzistenci (platnost, povinná pole) a propaguje chyby do UI pomocí `ObservableValidator`.【F:Veriado.WinUI/ViewModels/Files/EditableFileDetailModel.cs†L1-L178】
- `FileDetailDialog.xaml` definuje `ContentDialog` s `x:Bind` vazbami, inline výpisem chyb, převodníkem pro datum platnosti a blokem souhrnných metadat.【F:Veriado.WinUI/Views/Files/FileDetailDialog.xaml†L1-L138】
- `DialogService` rozpozná dialogové viewmodely (`IDialogAware`), přiřadí odpovídající view přes `IDialogViewFactory` a řídí jejich životní cyklus včetně asynchronního zavření.【F:Veriado.WinUI/Services/DialogService.cs†L1-L141】
- Aplikační `FileService` sjednocuje načtení detailu, přejmenování, update metadat i platnosti a převádí chybové stavy na doménově smysluplné výjimky pro UI.【F:Veriado.Services/Files/FileService.cs†L1-L188】
- `FileDetailDto` v aplikační vrstvě poskytuje konzistentní přenosový objekt pro detail a editaci dokumentu, včetně verzí a platnosti.【F:Veriado.Application/Files/Contracts/FileDetailDto.cs†L1-L33】
- `IDialogViewFactory` + `FileDetailDialogFactory` umožňují DI konstruovat ContentDialogy s odpovídajícími viewmodely bez service-locator patternu.【F:Veriado.WinUI/Services/Abstractions/IDialogViewFactory.cs†L1-L12】【F:Veriado.WinUI/Services/DialogFactories/FileDetailDialogFactory.cs†L1-L25】

## Požadavky
- .NET 8 SDK
- Windows 10 19041+ pro běh WinUI klienta (x86/x64/ARM64)
- SQLite 3 (distribuováno přes Microsoft.Data.Sqlite)

## Lokální spuštění
1. Obnovte závislosti: `dotnet restore`.
2. Sestavte solution (WinUI projekt vyžaduje Windows SDK): `dotnet build Veriado.sln`.
3. Spusťte testy aplikační vrstvy: `dotnet test Veriado.sln`.
4. Pro spuštění klienta na Windows otevřete `Veriado.sln` ve Visual Studio 2022 (17.10+) nebo použijte `dotnet build` + `AppInstaller` pro MSIX z `Veriado.WinUI`.

Při prvním startu `AppHost` vyhodnotí cestu k databázi, vytvoří potřebné složky a spustí migrace. Cestu lze přepsat v `appsettings.json` (`Infrastructure:DbPath`) nebo proměnnou `VERIADO_DESIGNTIME_DB_PATH` pro design-time nástroje.【F:Veriado.WinUI/AppHost.cs†L70-L82】【F:Veriado.Infrastructure/Persistence/Connections/SqlitePathResolver.cs†L34-L78】

## Testování
Testovací projekt `Veriado.Application.Tests` ověřuje šíření doménových událostí, rollback při selhání handleru i konzistenci logů a reindexační fronty.【F:Veriado.Application.Tests/Infrastructure/DomainEventsInterceptorTests.cs†L50-L154】 Spustíte jej příkazem `dotnet test` (viz výše).

## Další zdroje
- [`docs/application-overview.md`](docs/application-overview.md) – funkční scénáře a tok UI.
- [`docs/architecture-review.md`](docs/architecture-review.md) – detailní architektonické poznámky a doporučení.
- [`MIGRATION_NOTES.md`](MIGRATION_NOTES.md) – poznámky k aktualizacím fulltextového schématu.

## Licence
Projekt je licencován pod MIT licencí (viz `LICENSE.txt`).
