# Přehled aplikace Veriado

Tento dokument shrnuje klíčové funkce desktopové aplikace Veriado, která kombinuje WinUI klienta, aplikační vrstvu s MediatR a perzistenci nad SQLite s fulltextovým vyhledáváním. Popisuje, jaké scénáře aplikace pokrývá a jak spolu jednotlivé části spolupracují.

## Životní cyklus aplikace a infrastruktura
- **Start a hostování** – `AppHost.StartAsync` sestaví `IHost`, zaregistruje služby UI, mapování, aplikační i infrastrukturní vrstvu, spočítá cestu k lokální databázi a před spuštěním zajistí migraci pomocí globálního mutexu. Po startu inicializuje "hot state" službu pro obnovu uživatelských preferencí.【F:Veriado.WinUI/AppHost.cs†L23-L105】
- **Rozložení vrstev** – WinUI projekty pracují přes služby z `Veriado.Services`, které mapují volání do aplikačních handlerů. Doménová vrstva v `Veriado.Domain` drží agregáty souboru včetně metadat, revizí obsahu a stavu fulltextového indexu.【F:Veriado.Services/Files/FileOperationsService.cs†L11-L132】【F:Veriado.Domain/Files/FileEntity.cs†L10-L123】

## Správa katalogu souborů
- **Přehledy a filtrování** – `FilesPageViewModel` poskytuje grid s stránkováním, kompletní sadu filtrů (typ souboru, autor, verze, velikost, platnost dokumentu, časová razítka) a sledování zdraví indexu. Uživatelské nastavení velikosti stránky a posledního dotazu se obnovuje z `HotStateService`.【F:Veriado.WinUI/ViewModels/Files/FilesPageViewModel.cs†L21-L157】
- **Detail souboru** – stránka načítá podrobné informace i plný metadatový profil souboru přes `IFileQueryService`, včetně indikace platnosti dokumentu nebo čekajících reindexací.【F:Veriado.Services/Files/FileQueryService.cs†L19-L56】【F:Veriado.WinUI/ViewModels/Files/FilesPageViewModel.cs†L65-L157】
- **Úpravy metadat a stavu** – `FileOperationsService` mapuje vstupy z UI na validační pipeline a MediatR příkazy. Umožňuje přejmenování, aktualizaci vlastních metadat, nastavení platnosti dokumentu, přepnutí režimu pouze pro čtení i aplikaci systémových metadat z NTFS.【F:Veriado.Services/Files/FileOperationsService.cs†L24-L121】
- **Obsah souboru** – `FileContentService` vrací jak metadatové informace, tak fyzickou cestu k souboru v řízeném úložišti. Umožňuje otevření v systémové aplikaci, zobrazení v Průzkumníku a export/kopírování na zvolenou cestu se zachováním metadat z NTFS, takže UI pracuje přímo s reálným souborem.【F:Veriado.Services/Files/FileContentService.cs†L24-L100】

## Fulltextové vyhledávání
- **Dotazy nad katalogem** – `SearchFacade` a aplikační služby skládají FTS5 dotazy, provádějí je nad indexem a vracejí výsledky mapované na DTO. Implementace ošetřuje prázdné dotazy a limituje počet vrácených položek podle požadavku UI.【F:Veriado.Services/Search/SearchFacade.cs†L11-L63】
- **Historie a oblíbené dotazy** – stejné facade zajišťují uložení posledních dotazů s počtem shod a správu oblíbených filtrů, které lze vyvolat jedním kliknutím a ihned přidat do historie hledání.【F:Veriado.Services/Search/SearchFacade.cs†L65-L94】
- **Filtrace datové mřížky** – číselník souborů (`FileGridQuery`) umí kombinovat fulltextové kandidáty s pokročilými filtry a hlídá maximální velikost stránky i počtu vyhodnocených kandidátů, aby UI reagovalo svižně i nad velkými korpusy.【F:Veriado.Services/Files/FileQueryService.cs†L19-L35】【F:Veriado.Application/UseCases/Queries/FileGrid/FileGridQueryOptions.cs†L1-L16】

## Import dokumentů
- **Hromadný import složek** – `ImportPageViewModel` umožňuje vybrat složku, nastavit rekurzi, velikost bufferu či maximální paralelismus a spustit streaming import s průběžnými statistikami, exportem logu a filtrováním chyb podle závažnosti.【F:Veriado.WinUI/ViewModels/Import/ImportPageViewModel.cs†L18-L124】
- **Zpracování na pozadí** – `ImportService` koordinuje pipeline se semafory proti kolizím importu a oprav indexu, normalizuje vstupní požadavky a překládá volby UI na příslušné příkazy v aplikační vrstvě. Legacy API pro jednorázový soubor zůstává z důvodu zpětné kompatibility, ale směřuje k postupnému nahrazení streamingem.【F:Veriado.Services/Import/ImportService.cs†L25-L119】
- **Konfigurovatelné limity** – kontrakt `ImportOptions` definuje limity velikosti souboru, paralelismus, retry politiku při zamčených souborech, zachování metadat i výchozího autora. ViewModel tato nastavení persistuje a validuje vůči možnostem UI.【F:Veriado.Contracts/Import/ImportOptions.cs†L1-L63】【F:Veriado.WinUI/ViewModels/Import/ImportPageViewModel.cs†L90-L124】

## Údržba a diagnostika
- **Zdraví aplikace** – `HealthService` publikuje diagnostické údaje o stavu infrastruktury i statistikách indexu. ViewModel souborové stránky je periodicky dotazuje a při odchylkách zobrazuje varování uživateli.【F:Veriado.Services/Diagnostics/HealthService.cs†L7-L24】【F:Veriado.WinUI/ViewModels/Files/FilesPageViewModel.cs†L25-L63】
- **Údržbové akce** – `MaintenanceService` spouští vacuum/optimize nad SQLite, ověřuje a opravuje fulltextový index a zajišťuje reindex po změně schématu. Importní služba jej využívá k reparačním běhům v případě nekonzistence indexu.【F:Veriado.Services/Maintenance/MaintenanceService.cs†L7-L41】【F:Veriado.Services/Import/ImportService.cs†L45-L78】

## Nastavení uživatelského prostředí
- **Preference UI** – stránka nastavení umožňuje měnit motiv aplikace, výchozí velikost stránky v gridu a poslední složku importu. Změny se okamžitě ukládají do `HotStateService`, takže se přenášejí mezi relacemi bez restartu klienta.【F:Veriado.WinUI/ViewModels/Settings/SettingsPageViewModel.cs†L6-L66】
- **Zážitky z práce** – `AppHost` registruje služby pro dialogy, schránku, náhledy i notifikace stavu, aby uživatel viděl průběh operací (import, změny metadat) a mohl reagovat na chyby přímo v UI.【F:Veriado.WinUI/AppHost.cs†L33-L77】

## Shrnutí hlavních scénářů
1. **Archivace dokumentu** – uživatel spustí import složky, který díky streamingové pipeline zvládá tisíce souborů se zachovanými NTFS metadaty. Po dokončení jsou položky viditelné v gridu a lze je dále editovat či označit jako jen pro čtení.【F:Veriado.Services/Import/ImportService.cs†L25-L119】【F:Veriado.Services/Files/FileOperationsService.cs†L24-L121】
2. **Vyhledání a analýza** – ve filtru gridu nebo samostatné vyhledávací liště se zadá dotaz, který `SearchFacade` přepočte na FTS5 výraz, uloží do historie a zobrazí výsledky včetně oblíbených dotazů pro rychlé opakování.【F:Veriado.Services/Search/SearchFacade.cs†L27-L94】【F:Veriado.Services/Files/FileQueryService.cs†L19-L56】
3. **Pravidelná údržba** – administrátor v UI spustí kontrolu indexu či vacuum, případně sleduje diagnostické přehledy. Při importech se automaticky blokuje souběžná oprava indexu, aby nedocházelo ke kolizím zápisů.【F:Veriado.Services/Maintenance/MaintenanceService.cs†L19-L41】【F:Veriado.Services/Import/ImportService.cs†L45-L78】

Tento přehled doplňuje architektonické poznámky v `architecture-review.md` a slouží jako rychlá orientace v tom, jaké funkce Veriado nabízí koncovým uživatelům.
