# Veriado

> "Pořádek v dokumentech, klid v práci."

![version](https://img.shields.io/badge/version-TODO-blue) ![license](https://img.shields.io/badge/license-MIT-green) ![platform](https://img.shields.io/badge/platform-Windows-blueviolet) ![dotnet-8](https://img.shields.io/badge/.NET-8.0-512BD4) ![winui](https://img.shields.io/badge/WinUI-desktop-9F3CFE)

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
**Požadavky:** Windows 10 nebo 11, .NET 8 Runtime/SDK, uživatelská práva pro instalaci.

```powershell
# volitelně: instalace .NET 8
dotnet --info

# stažení balíčku (TODO: doplnit odkaz)
# rozbalení a spuštění
Veriado.Setup.exe
```

**První spuštění:** zvolte nebo vytvořte katalog, spusťte první import složky a ověřte základní vyhledávání.

**Demo data:** TODO odkázat na balíček s ukázkovými dokumenty.

## Pro ICT/IT
**Platforma:** .NET 8, WinUI desktop, lokální SQLite s fulltextovým modulem FTS5.

**Architektura vrstev:**
- **Domain** – doménové agregáty, hodnotové objekty a události.
- **Application** – MediatR handlery, validační/logovací pipeline, `AmbientRequestContext`.
- **Mapping** – mapování DTO ↔ příkazy, validace vstupů.
- **Infrastructure** – přístup k SQLite, repozitáře, transakce a správa souborů.
- **Services** – služby pro zdraví, údržbu, import a adaptéry pro UI.
- **WinUI klient** – desktopové rozhraní s `HotState` pro per-user personalizaci.

**Klíčové komponenty:**
- `FileEntity` – metadata dokumentu, verze, platnost, stav indexace.
- `FileOperationsService` – editace, přejmenování, synchronizace NTFS metadat a validace.
- `SearchFacade` – správa FTS dotazů, historie a oblíbených filtrů.
- `MaintenanceService`, `HealthService` – diagnostika, vacuum, reindex, metriky.
- Transakční repozitář + domain event log – agregát a události v jedné transakci.

**Bezpečnost a audit:** oddělený binární obsah od metadat, auditovatelná historie změn, připravené háčky pro napojení na DLP.

**Integrace a rozšiřitelnost:** otevřená architektura připravená na SSO, DLP, ERP – bez proprietárních závislostí.

**Požadavky na provoz:** bez serveru, nízké nároky na CPU/RAM, možnost provozu na noteboocích i VDI.

## Konfigurace
- **Umístění katalogu/databáze:** volba při prvním spuštění, možnost přemapovat v nastavení.
- **Nastavení indexace:** plánovaná reindexace, limity importu, frekvence vacuum – dostupné v administračním panelu.
- **Logování:** úroveň logů a cílová složka definovatelná v konfiguračním souboru.
- **Uživatelské preference (HotState):** motiv aplikace, velikost stránky výsledků, výchozí filtry uložené per uživatel.

## Práce s daty a import
1. Otevřete modul importu a zvolte složku nebo archiv ke zpracování.
2. Mapujte pole na metadata (např. platnost, typ dokumentu) a spusťte import.
3. Sledujte průběh, řešte chyby pomocí filtru neúspěšných položek, případně opakujte pouze chybové položky.

Metadata se ukládají spolu s historií úprav; chyby importu se logují s detailním popisem a návrhem nápravy. Doporučujeme udržovat složky podle agendy (např. smlouvy, certifikace) a používat konzistentní názvy souborů pro snadnější mapování.

## Zdravotní kontrola a údržba
Zdravotní panel zobrazuje stav databáze, velikost indexu, počty dokumentů podle platnosti a poslední údržbové akce. Údržbové akce zahrnují reindex pro obnovu fulltextu, vacuum pro optimalizaci dat a diagnostiku pro kontrolu integrity. Doporučujeme je spouštět po velkém importu nebo při poklesu výkonu vyhledávání.

## Snímky obrazovky
- ![Ukázka vyhledávání](./docs/screenshots/search.png) – TODO doplnit aktuální snímek vyhledávacího rozhraní.
- ![Platnost dokumentů](./docs/screenshots/validity.png) – TODO doplnit snímek přehledu platnosti.
- ![Zdravotní panel](./docs/screenshots/health.png) – TODO doplnit snímek zdravotní konzole.

## Roadmap
- Rozšíření integračních konektorů (SSO, DLP, ERP).
- Pokročilé reporty a exporty pro compliance.
- Automatizované připomínky platnosti přes e-mail.
- TODO odkázat na GitHub Issues/Projects pro aktuální plán.

## Nápověda a podpora
- **Chyby a incidenty:** nahlaste přes GitHub Issues s šablonou „Bug report“.
- **Požadavky na funkce:** použijte šablonu „Feature request“.
- **Kontakt:** TODO doplnit e-mail nebo webový formulář.

## Přispívání
1. Forkněte repozitář a vytvořte feature branch (`feature/jmeno-funkce`).
2. Dodržujte konvence commitů (např. `feat:`, `fix:`, `docs:`) a připojte popis změny.
3. Před PR ověřte build a testy, vyplňte checklist v šabloně PR.
4. Respektujte stávající code style (viz poznámky v adresáři `docs/`).

## Licence
Projekt je licencován pod MIT licencí – viz [LICENSE.txt](LICENSE.txt).

## Poznámky k značce a logu
Logo a název používejte v souladu s brand guidelines (TODO odkázat na dokument). Vektorové logo je k dispozici v souboru `./docs/brand/veriado-logo.svg`.

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
