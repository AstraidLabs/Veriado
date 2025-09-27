# Veriado

## Úvod
Veriado je desktopová aplikace pro Windows, která katalogizuje dokumenty, ukládá jejich binární obsah a metadata do lokální databáze a staví nad nimi plnotextové vyhledávání s moderním WinUI rozhraním.【F:Veriado.Domain/Files/FileEntity.cs†L13-L177】【F:Veriado.Domain/Files/FileContentEntity.cs†L6-L57】【F:Veriado.Infrastructure/Persistence/Options/InfrastructureOptions.cs†L13-L34】【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L11-L50】【F:Veriado.WinUI/Views/FilesView.xaml†L1-L45】

## Popis aplikace
### Klíčové vlastnosti
- **Správa životního cyklu dokumentů** – aplikace poskytuje kompletní sadu use-case příkazů pro vytváření, přejmenování, úpravy metadat, práci s rozšířenými údaji i přepnutí režimu jen pro čtení a platnosti dokumentů.【F:Veriado.Application/README.md†L3-L21】 Tyto operace jsou dostupné přes servisní vrstvu a WinUI detail, který dovoluje přejmenovat, aktualizovat metadata, řídit platnost či režim pouze pro čtení i sdílet snippet obsahu.【F:Veriado.Services/Files/FileOperationsService.cs†L22-L180】【F:Veriado.WinUI/ViewModels/Files/FileDetailViewModel.cs†L14-L310】
- **Pokročilé vyhledávání** – dotazy se skládají do bezpečných FTS5 výrazů a trigramových dotazů, takže uživatelé mohou kombinovat přesné a fuzzy vyhledávání, zatímco výsledky se mapují do přehledných DTO.【F:Veriado.Application/Search/FtsQueryBuilder.cs†L8-L111】【F:Veriado.Application/Search/TrigramQueryBuilder.cs†L9-L161】【F:Veriado.Services/Search/SearchFacade.cs†L11-L81】
- **Historie, oblíbené dotazy a personalizace** – okno se seznamem souborů nabízí rychlý přístup k historii a oblíbeným dotazům, přičemž stav posledního hledání a velikost stránek se perzistují v nastavení uživatele.【F:Veriado.WinUI/ViewModels/Files/FilesGridViewModel.cs†L18-L142】【F:Veriado.WinUI/Services/HotStateService.cs†L10-L137】【F:Veriado.Infrastructure/Search/SearchHistoryService.cs†L14-L132】
- **Importy a dávkové zpracování** – služba importu umí načítat jednotlivé soubory i celé složky, respektuje nastavenou paralelizaci, uživatelský limit velikosti souboru a na pozadí vytváří a reindexuje dokumenty včetně následných kroků po importu.【F:Veriado.Services/Import/ImportService.cs†L22-L356】【F:Veriado.Contracts/Import/ImportFolderRequest.cs†L3-L49】【F:Veriado.WinUI/ViewModels/Import/ImportPageViewModel.cs†L41-L371】
- **Textové náhledy a práce s obsahem** – aplikace generuje a cacheuje textové náhledy, pro binární formáty nabízí výstižné fallbacky a dovoluje export obsahu na disk nebo sdílení úryvků přímo z UI.【F:Veriado.WinUI/Services/PreviewService.cs†L11-L138】【F:Veriado.Services/Files/FileContentService.cs†L12-L83】【F:Veriado.WinUI/ViewModels/Files/FileDetailViewModel.cs†L238-L270】
- **Extraktory pro běžné formáty** – kompozitní textový extractor podporuje PDF, Office i OpenDocument soubory a v případě neúspěchu přepíná na bezpečný fallback bez nutnosti externích služeb.【F:Veriado.Infrastructure/README.md†L3-L18】
- **Údržba a integrita indexu** – servisní vrstva inicializuje infrastrukturu, spouští VACUUM/OPTIMIZE a umí ověřit či opravit konzistenci fulltextového indexu na vyžádání.【F:Veriado.Services/Maintenance/MaintenanceService.cs†L11-L46】【F:Veriado.Application/UseCases/Maintenance/VerifyAndRepairFulltextHandler.cs†L10-L40】

### Architektura
Projekt je rozdělen do vrstev podle principů doménově řízeného návrhu: čistá doména žije ve `Veriado.Domain`, kontrakty pro klientske vrstvy v `Veriado.Contracts`, aplikační logika je vyjádřena jako MediatR use-casy v `Veriado.Application` a mapování mezi doménou a DTO obstarává samostatný projekt s AutoMapper profily a anti-corruption vrstvou.【F:docs/ARCH-DESIGN.md†L3-L13】【F:Veriado.Application/README.md†L3-L21】【F:Veriado.Mapping/README.md†L1-L28】 Infrastruktura využívá EF Core a SQLite včetně FTS5 indexu, přičemž front-end WinUI projekt funguje jako hostitel služeb a poskytuje moderní desktopové UI.【F:Veriado.Infrastructure/Veriado.Infrastructure.csproj†L1-L29】【F:Veriado.WinUI/Veriado.csproj†L1-L49】

## Technologie
- .NET 8.0 pro aplikační, infrastrukturní i servisní projekty (C# 12, nullable reference types).【F:Veriado.Application/Veriado.Appl.csproj†L1-L17】【F:Veriado.Infrastructure/Veriado.Infrastructure.csproj†L1-L29】【F:Veriado.Services/Veriado.Services.csproj†L1-L13】
- WinUI 3 a Windows App SDK 1.8 pro desktopové rozhraní cílené na Windows 10 build 17763+ (x86, x64, ARM64).【F:Veriado.WinUI/Veriado.csproj†L1-L49】
- MediatR, AutoMapper a FluentValidation pro orchestraci use-case příkazů, mapování a validace vstupů.【F:Veriado.Application/Veriado.Appl.csproj†L1-L13】【F:Veriado.Mapping/README.md†L1-L17】
- Entity Framework Core 9, SQLite (včetně FTS5), PdfPig a DocumentFormat.OpenXml pro perzistenci a extrakci obsahu.【F:Veriado.Infrastructure/Veriado.Infrastructure.csproj†L10-L29】【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L11-L40】【F:Veriado.Infrastructure/README.md†L3-L18】
- CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting a vlastní servisní vrstva pro MVVM logiku, navigaci a hostování komponent v desktopové aplikaci.【F:Veriado.WinUI/Veriado.csproj†L38-L41】【F:Veriado.WinUI/ViewModels/Files/FilesGridViewModel.cs†L18-L142】【F:Veriado.Services/Veriado.Services.csproj†L1-L13】

## Předpokládané systémové nároky
| Oblast | Minimum | Doporučeno | Důvod |
| --- | --- | --- | --- |
| Operační systém | Windows 10 17763 (x64/ARM64) | Windows 11 aktuální build | WinUI projekt cílí na `net8.0-windows10.0.19041.0` s minimem 17763 a podporou více architektur.【F:Veriado.WinUI/Veriado.csproj†L1-L12】 |
| .NET runtime | .NET 8 Desktop Runtime | .NET 8 Desktop Runtime + Windows App SDK 1.8 | Projekty cílí na net8.0 a WinUI 3 knihovny.【F:Veriado.WinUI/Veriado.csproj†L1-L41】【F:Veriado.Application/Veriado.Appl.csproj†L1-L13】 |
| CPU | 2 jádra | 4+ jader | Import dokáže paralelně zpracovávat soubory podle `MaxDegreeOfParallelism`, proto vícejádrový CPU urychlí dávky.【F:Veriado.Services/Import/ImportService.cs†L76-L116】【F:Veriado.Contracts/Import/ImportFolderRequest.cs†L25-L29】 |
| RAM | 4 GB | 8 GB+ | Při importu se soubory načítají celé do paměti (`File.ReadAllBytesAsync`), což u větších dokumentů vyžaduje dostatek RAM.【F:Veriado.Services/Import/ImportService.cs†L187-L199】 |
| Úložiště | 1× velikost katalogu | SSD + prostor ≥ 2× katalog | Obsah je kopírován do SQLite (včetně hashů) a indexován; nastavení `DbPath` a `MaxContentBytes` definuje umístění a limity uložených binárních dat.【F:Veriado.Domain/Files/FileContentEntity.cs†L13-L56】【F:Veriado.Infrastructure/Persistence/Options/InfrastructureOptions.cs†L13-L34】【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L22-L40】 |

## F.A.Q.
**Jak přidám nový dokument do katalogu?**
Import můžete spustit buď pro jednotlivý soubor (`ImportFileAsync`), nebo pro celou složku, kde služba podle nastavení projde podadresáře, načte obsah a postará se o reindexaci a navazující use-case příkazy.【F:Veriado.Services/Import/ImportService.cs†L45-L185】【F:Veriado.Application/README.md†L3-L21】 

**Jak se udržuje konzistence fulltextu?**
Inicializační rutina spouští migrace a optimalizaci a kdykoli lze zavolat `VerifyAndRepairAsync`, který projde index, zjistí chybějící či osiřelé záznamy a podle nastavení je doplní.【F:Veriado.Services/Maintenance/MaintenanceService.cs†L25-L46】【F:Veriado.Application/UseCases/Maintenance/VerifyAndRepairFulltextHandler.cs†L10-L40】 

**Kde se berou náhledy obsahu?**
WinUI využívá `PreviewService`, který si načte obsah přes `IFileContentService`, uloží výsledek do cache a pokud soubor není textový, zobrazí přehlednou textovou indikaci typu souboru.【F:Veriado.WinUI/Services/PreviewService.cs†L11-L138】【F:Veriado.Services/Files/FileContentService.cs†L12-L83】 

**Jak funguje historie a oblíbené vyhledávání?**
`SearchFacade` vrací výsledky z query služby a ukládá každé spuštění do sqlite tabulky historie; uživatelské UI drží seznam oblíbených i historii v paměti a umožňuje rychlé opakování dotazů.【F:Veriado.Services/Search/SearchFacade.cs†L11-L81】【F:Veriado.Infrastructure/Search/SearchHistoryService.cs†L25-L121】【F:Veriado.WinUI/ViewModels/Files/FilesGridViewModel.cs†L46-L142】 

**Lze z aplikace vyexportovat uložený soubor?**
Ano, `FileContentService.SaveContentToDiskAsync` uloží aktuální binární obsah na zvolenou cestu a vytvoří chybějící složky.【F:Veriado.Services/Files/FileContentService.cs†L52-L83】

## Nejčastější problémy a jejich řešení
- **Fulltext nevrací výsledky po importu.** Spusťte údržbový příkaz `VerifyAndRepairAsync` (případně `ReindexFileCommand`) a povolte extrakci obsahu, aby se index doplnil.【F:Veriado.Services/Maintenance/MaintenanceService.cs†L31-L46】【F:Veriado.Application/UseCases/Maintenance/VerifyAndRepairFulltextHandler.cs†L25-L40】
- **Import velké složky zahlcuje systém.** Omezte `MaxDegreeOfParallelism` v `ImportFolderRequest` nebo rozdělte import na menší dávky; paralelní import využívá všechna dostupná vlákna.【F:Veriado.Services/Import/ImportService.cs†L76-L116】【F:Veriado.Contracts/Import/ImportFolderRequest.cs†L25-L37】
- **Soubor je přeskočen kvůli limitu velikosti.** Zvyšte hodnotu „Max. velikost souboru (MB)“ v dialogu importu nebo ji ponechte na 0 pro neomezený import; změna se uloží mezi uživatelská nastavení.【F:Veriado.WinUI/Views/Import/ImportPage.xaml†L63-L87】【F:Veriado.WinUI/Services/HotStateService.cs†L16-L140】【F:Veriado.Contracts/Import/ImportFolderRequest.cs†L3-L49】
- **Náhled je prázdný nebo zobrazí pouze ikonu.** Služba náhledů vrací text jen u podporovaných MIME; binární či neznámé formáty se zobrazují jako fallback popis, což je očekávané chování.【F:Veriado.WinUI/Services/PreviewService.cs†L24-L138】
- **Aplikace hlásí, že nelze otevřít databázi.** Zkontrolujte nastavení `DbPath` a práva k souboru; bez platného connection stringu SQLite indexer nefunguje a vyhazuje chybu inicializace.【F:Veriado.Infrastructure/Persistence/Options/InfrastructureOptions.cs†L13-L34】【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L43-L50】
