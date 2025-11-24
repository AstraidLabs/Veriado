# Veriado

> "Pořádek v dokumentech, klid v práci."

![version](https://img.shields.io/badge/version-dev-blue) ![license](https://img.shields.io/badge/license-MIT-green) ![platform](https://img.shields.io/badge/platform-Windows-blueviolet) ![dotnet-8](https://img.shields.io/badge/.NET-8.0-512BD4) ![winui](https://img.shields.io/badge/WinUI-desktop-9F3CFE)

Veriado je desktopová aplikace pro firemní katalogizaci dokumentů, která kombinuje plnotextové vyhledávání s pečlivou správou metadat a platnosti. Je určena pro týmy, které potřebují mít důležité smlouvy, směrnice a záznamy okamžitě po ruce. Aplikace šetří čas díky rychlému vyhledávání a uloženým filtrům, hlídá platnost dokumentů a tím minimalizuje rizika. Díky lokálnímu provozu bez serveru přináší nízké provozní náklady a rychlé nasazení.

## Obsah
- [Pro firmy](#pro-firmy)
- [Přehled funkcí](#přehled-funkcí)
- [Typické scénáře](#typické-scenáře)
- [Rychlý start](#rychlý-start)
- [Pro ICT/IT](#pro-ictit)
- [Konfigurace](#konfigurace)
- [Práce s daty a import](#práce-s-daty-a-import)
- [Zdravotní kontrola a údržba](#zdravotní-kontrola-a-údržba)
- [Snímky obrazovky](#snímky-obrazovky)
- [Roadmap](#roadmap)
- [Nápověda a podpora](#nápověda-a-podpora)
- [Přispívání](#přispívání)
- [Licence](#licence)
- [Poznámky k značce a logu](#poznámky-k-značce-a-logu)
- [FAQ](#faq)
- [Proč Veriado](#proč-veriado)

## Pro firmy
Rychle najdu vše důležité: plnotextové vyhledávání reaguje během vteřin a uložené filtry zkracují cestu k nejčastějším dotazům. Pořádek a kontrola platnosti: systém hlídá expiraci dokumentů, zaznamenává změny a nabízí historii úprav. Méně ruční práce: hromadný import, automatické třídění a předvyplněná metadata šetří hodiny manuálních zásahů. Jednoduché ovládání: moderní WinUI prostředí se přizpůsobí preferencím uživatelů a pamatuje jejich volby. Nízké náklady a rychlé nasazení: běží lokálně bez serveru, nasazení trvá jen pár minut. Výsledek? Méně času stráveného hledáním, nižší náklady na správu dokumentů a spokojenější tým.

## Přehled funkcí
- **Fulltextové vyhledávání (FTS)** – okamžité nalezení textu v dokumentech včetně morfologického rozšíření.
- **Uložené dotazy a oblíbené filtry** – personalizované přehledy, které stačí jednou nastavit a příště jen otevřít.
- **Evidence platnosti dokumentů** – sledování expirace, upozornění a reporty pro včasné prodloužení.
- **Verze a auditovatelnost změn** – každá úprava se ukládá s historií a komentářem.
- **Hromadný import složek/archivů** – import tisíců souborů najednou s doplněním metadat.
- **Zdravotní panel, vacuum/reindex** – přehled kondice databáze a samoobslužné údržbové akce.
- **Personalizace (filtry, zobrazení, motiv)** – uživatelé si přizpůsobí vzhled, sloupce i výchozí filtrování.
- **Offline/lokální provoz** – vše běží na pracovním počítači, bez závislosti na externí infrastruktuře.

## Typické scénáře
- **Digitalizace a archivace** – hromadně importujte tisíce dokumentů včetně metadat a připojených popisů.
- **Compliance / audity** – připravte uložené dotazy pro opakované kontroly platnosti a úplnosti dokumentace.
- **Proaktivní údržba** – sledujte zdravotní statistiky, plánujte reindex nebo vacuum a držte databázi v kondici.

## Rychlý start
**Požadavky:** Windows 10 nebo 11, .NET 8 SDK, povolené spouštění WinUI aplikací.

```powershell
git clone https://github.com/.../Veriado.git
cd Veriado
dotnet restore
dotnet build Veriado.sln

# spuštění klienta (vyžaduje Windows)
dotnet run --project Veriado.WinUI
```

**První spuštění:** při startu `AppHost` vypočítá cestu k lokální SQLite databázi, získá globální mutex, provede migrace a inicializuje per-user "hot state" s uživatelskými preferencemi. Poté otevřete modul importu, zpracujte složku a ověřte, že grid souborů vrací výsledky vyhledávání.【F:Veriado.WinUI/AppHost.cs†L23-L105】

**Demo data:** zatím nejsou součástí repozitáře; import ověřte na vlastních souborech nebo interních vzorcích.

## Pro ICT/IT
**Platforma:** .NET 8, WinUI desktop, lokální SQLite s fulltextovým modulem FTS5.

**Architektura vrstev:**
- **Domain** – doménové agregáty, hodnotové objekty a události (`FileEntity` drží metadata, platnost a stav indexace).
- **Application** – MediatR handlery, validační/logovací pipeline a ambientní kontext požadavku (`AmbientRequestContext`).
- **Mapping** – mapování DTO ↔ příkazy a `WriteMappingPipeline` pro orchestraci zápisů.
- **Infrastructure** – EF Core/SQLite s FTS5, repozitáře, transakce a inicializace schématu při startu.
- **Services** – API vrstva pro WinUI (import, vyhledávání, údržba, práce se soubory) a koordinace pipeline.
- **WinUI klient** – desktopové rozhraní s `HotState` pro per-user personalizaci a návrat k posledním filtrům.

**Klíčové komponenty:**
- `FileOperationsService` – přejmenování, úprava metadat, nastavení platnosti a synchronizace NTFS vlastností se validací.
- `FileContentService` – otevírání souborů z katalogu, export kopie s přenesením metadat a návazností na UI službu pro náhledy.
- `SearchFacade` – skládání FTS dotazů, uložení do historie a oblíbených filtrů a návrat výsledků pro grid.
- `ImportService` – streamovaný hromadný import složek s limity paralelismu, semafory proti kolizím a reparačními běhy indexu.
- `MaintenanceService` a `HealthService` – diagnostika, vacuum/reindex a metriky pro zdraví databáze.
- Transakční repozitář + domain event log – agregát a události v jedné transakci nad SQLite.

**Bezpečnost a audit:** oddělený binární obsah od metadat, auditovatelná historie změn, připravené háčky pro napojení na DLP.

**Integrace a rozšiřitelnost:** otevřená architektura připravená na SSO, DLP, ERP – bez proprietárních závislostí.

**Požadavky na provoz:** bez serveru, nízké nároky na CPU/RAM, možnost provozu na noteboocích i VDI.

## Konfigurace
- **Umístění katalogu/databáze:** volba při prvním spuštění, možnost přemapovat v nastavení.
- **Nastavení indexace:** plánovaná reindexace, limity importu, frekvence vacuum – dostupné v administračním panelu.
- **Logování:** úroveň logů a cílová složka definovatelná v konfiguračním souboru.
- **Uživatelské preference (HotState):** motiv aplikace, velikost stránky výsledků, výchozí filtry uložené per uživatel.

## Práce s daty a import
1. Otevřete modul importu a zvolte složku ke zpracování, nastavte rekurzi, velikost bufferu a maximální paralelismus.
2. Spusťte streamovaný import; průběžné statistiky vidíte ve ViewModelu, včetně exportu logu a filtrování chyb.
3. Po dokončení otevřete detail souboru, kde uvidíte metadata, platnost a stav indexace, případně upravte položky nebo označte jako jen pro čtení.

Metadata se ukládají spolu s historií úprav a stavem indexace; chyby importu se logují s detailní závažností a lze je znovu zpracovat bez restartu klienta.

## Zdravotní kontrola a údržba
Zdravotní panel zobrazuje stav databáze, velikost indexu, počty dokumentů podle platnosti a poslední údržbové akce. Údržbové akce zahrnují reindex pro obnovu fulltextu, vacuum pro optimalizaci dat a diagnostiku pro kontrolu integrity. Doporučujeme je spouštět po velkém importu nebo při poklesu výkonu vyhledávání.

## Snímky obrazovky
- ![Ukázka vyhledávání](./docs/screenshots/search.png) – ukázka FTS vyhledávání nad katalogem.
- ![Platnost dokumentů](./docs/screenshots/validity.png) – přehled platnosti dokumentů v detailu položky.
- ![Zdravotní panel](./docs/screenshots/health.png) – diagnostika velikosti indexu a posledních údržbových akcí.

## Roadmap
- Rozšíření integračních konektorů (SSO, DLP, ERP).
- Pokročilé reporty a exporty pro compliance.
- Automatizované připomínky platnosti přes e-mail.
- Aktuální plán a backlog sledujte v GitHub Issues/Projects.

## Nápověda a podpora
- **Chyby a incidenty:** nahlaste přes GitHub Issues s šablonou „Bug report“.
- **Požadavky na funkce:** použijte šablonu „Feature request“.
- **Kontakt:** info@veriado.example (technické dotazy, roadmapa, onboarding).

## Přispívání
1. Forkněte repozitář a vytvořte feature branch (`feature/jmeno-funkce`).
2. Dodržujte konvence commitů (např. `feat:`, `fix:`, `docs:`) a připojte popis změny.
3. Před PR ověřte build a testy, vyplňte checklist v šabloně PR.
4. Respektujte stávající code style (viz poznámky v adresáři `docs/`).

## Licence
Projekt je licencován pod MIT licencí – viz [LICENSE.txt](LICENSE.txt).

## Poznámky k značce a logu
Logo a název používejte v souladu s brand guidelines (dokument bude zveřejněn spolu s první veřejnou verzí). Vektorové logo je k dispozici v souboru `./docs/brand/veriado-logo.svg`.

## FAQ
**Potřebuji server?**
Ne, Veriado běží lokálně na pracovním počítači bez potřeby serverové infrastruktury.

**Jak rychle to nasadíme?**
Stačí stáhnout instalační balíček a během několika minut máte funkční katalog.

**Lze importovat celé adresáře?**
Ano, hromadný import podporuje celé složky i archivy se zachováním struktury.

**Jak funguje vyhledávání?**
Používá SQLite FTS5, takže vyhledává v obsahu i metadatech a reaguje během vteřin.

**Umí to hlídat platnost dokumentů?**
Ano, u každého dokumentu je evidence platnosti a upozornění na expiraci.

**Jak řešíte bezpečnost a audit?**
Metadata a binární obsah jsou odděleny, změny se logují s historií a připravenými audity.

**Co když chci integraci se SSO/ERP?**
Architektura je připravena pro napojení na SSO a ERP, integrace lze doplnit přes rozšiřující služby.

## Proč Veriado
- Úspora času díky rychlému vyhledávání a uloženým filtrům.
- Nízké TCO díky lokálnímu provozu bez serveru.
- Jednoduché užívání s personalizovaným rozhraním.
- Silná auditovatelnost, evidence platnosti a historie změn.
