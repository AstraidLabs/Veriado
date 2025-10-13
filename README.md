# Veriado

## Strategický přehled
Veriado je moderní desktopová aplikace pro Windows, která firmám i profesionálům přináší pořádek do firemních archívů, projektových složek a sdílených úložišť. Transformuje neuspořádané kolekce dokumentů do přehledné znalostní základny, kterou lze bezpečně vyhledávat, třídit a sdílet napříč týmy. Aplikace kombinuje robustní technologické jádro postavené na .NET 8 s intuitivním WinUI rozhraním a díky tomu nabízí zkušenost připomínající „firemní Google“, avšak s plnou kontrolou nad daty.

## Poslání a přínos
- **Maximální viditelnost firemního know-how.** Veriado sjednocuje různé typy souborů, ukládá je do zabezpečené databáze a obohacuje o metadata, která lze snadno upravovat a sdílet.
- **Zrychlení rozhodování.** Díky fulltextovému vyhledávání, historii dotazů a chytrým filtrům se uživatelé dostanou ke klíčovým dokumentům během sekund, nikoli minut.
- **Podpora řízené spolupráce.** Režimy jen pro čtení, oblíbené dotazy i sdílení náhledů umožňují bezpečně distribuovat znalosti napříč odděleními bez nutnosti cloudových služeb.

## Jak Veriado funguje
1. **Import dokumentů** – Uživatel zvolí soubory nebo složky a Veriado je paralelně zpracuje, uloží binární obsah i metadata a připraví je k vyhledávání.
2. **Automatické obohacení** – Textové extraktory převádějí PDF, Office či OpenDocument formáty na čitelné náhledy, které se cacheují a zpřístupňují bez externích služeb.
3. **Fulltextový index** – Aplikace buduje vysoce výkonný FTS5 index v SQLite, který poskytuje přesné dotazy s bohatým skórováním.
4. **Práce s obsahem** – WinUI rozhraní nabízí rychlé náhledy, aktualizaci metadat, export obsahu a sdílení snippetů.
5. **Pravidelná údržba** – Vestavěné rutiny kontrolují integritu indexu, umožňují reindexaci a optimalizaci úložiště.

## Dopad na produktivitu
- **Méně času hledáním, více času tvorbou.** Automatizované vyhledávání a personalizované dotazy zkracují čas strávený hledáním dokumentů o desítky procent.
- **Bezpečná centralizace.** Organizace spravují citlivá data lokálně, s plnou kontrolou nad životním cyklem dokumentů i historií změn.
- **Okamžitý přehled pro management.** Dashboardy souborů, historie importů a metadata poskytují manažerům data pro reporting a audit.
- **Škálovatelný růst.** Díky dávkovým importům a konfigurovatelné paralelizaci zvládne aplikace obsloužit malé týmy i rozsáhlé archivy.

## Klíčové funkce
- **Správa životního cyklu** – Tvorba, přejmenování, aktualizace metadat, režimy platnosti i jen pro čtení v několika krocích.
- **Vyhledávání nové generace** – Přesné fulltextové dotazy doplněné historií a chytrými filtry pomáhají zachytit kontext.
- **Inteligentní náhledy** – Textové výtahy i fallback popisy informují o obsahu souborů ještě před jejich otevřením.
- **Import a automatizace** – Dávkové zpracování složek, limity velikosti souborů a následné workflow po importu.
- **Údržba a monitoring** – Kontrolní nástroje pro ověření konzistence indexu, optimalizaci databáze a audit aktivit.

## Technologický základ
- **.NET 8 & C# 12** – Výkon, bezpečnost a dlouhodobá podpora.
- **SQLite s FTS5** – Rychlý fulltextový engine s možností lokálního nasazení bez serverové infrastruktury.
- **WinUI 3 & Windows App SDK** – Moderní nativní prostředí pro Windows 10+ s podporou x86, x64 i ARM64.
- **MediatR, AutoMapper, FluentValidation** – Prověřené knihovny pro orchestraci use-case scénářů, mapování dat a validace.
- **CommunityToolkit.Mvvm & Microsoft.Extensions.Hosting** – Stabilní základ pro MVVM logiku, navigaci a hostování služeb.

## FTS5-only backend
- **Datový model.** Fulltextová tabulka `file_search` je navázána na tabulku `DocumentContent`, která ukládá normalizovaný titul, autora, MIME, sumarizovaný text metadat a JSON payload. Triggery `dc_ai/dc_au/dc_ad` garantují, že jakýkoli INSERT/UPDATE/DELETE v `DocumentContent` se okamžitě promítne do FTS bez manuálních zásahů.【F:Veriado.Infrastructure/Persistence/Schema/Fts5.sql†L1-L36】
- **Stavba dotazů.** `SearchQueryBuilder` podporuje boolovské operátory (`AND`, `OR`, `NOT`), fráze v uvozovkách, prefixové hledání (`term*`), scoped výrazy (`title:report*`) i časové/velikostní filtry. Žádná fuzzy logika ani trigramy nejsou k dispozici, protože SQLite FTS5 se stará o přesné porovnání tokenů.【F:Veriado.Application/Search/SearchQueryBuilder.cs†L1-L240】
- **Řazení výsledků.** Vyhodnocení probíhá čistě přes `bm25(file_search, titleWeight, authorWeight, mimeWeight, metadataTextWeight, metadataWeight)`. Výchozí váhy zvýhodňují titulky a autory; hodnoty lze upravit v `SearchScoreOptions`. Výsledky lze dále modifikovat vlastním SQL výrazem nebo managed delegátem pro finální skóre.【F:Veriado.Application/Search/SearchQueryPlan.cs†L45-L86】【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L320-L371】
- **Reindexace.** Pro hromadný rebuild stačí jednorázově spustit `INSERT INTO file_search(file_search) VALUES('rebuild');`. Triggery poté automaticky znovu naplní FTS z `DocumentContent`. Služba integrity tento krok provádí i během oprav, takže není nutné spouštět externí skripty.【F:Veriado.Infrastructure/Integrity/FulltextIntegrityService.cs†L380-L404】
- **PRAGMA doporučení.** Každé připojení používá `journal_mode=WAL`, `synchronous=NORMAL`, `temp_store=MEMORY`, `mmap_size=268435456` a `page_size=4096`. Při masivním rebuildu lze `synchronous` dočasně přepnout na `OFF` a po dokončení vrátit na `NORMAL`. Tato nastavení aplikuje `SqlitePragmaHelper` automaticky.【F:Veriado.Infrastructure/Persistence/SqlitePragmaHelper.cs†L9-L90】
- **Observabilita a audit.** Periodický `IndexAuditBackgroundService` (výchozí interval 4 hodiny) spouští `IndexAuditor`, který porovnává `DocumentContent` s doménovými soubory a plánuje reindexace chybějících dokumentů. Telemetrie zaznamenává latence dotazů, počty reindexovaných položek i velikost dead-letter fronty.【F:Veriado.Infrastructure/Integrity/IndexAuditBackgroundService.cs†L1-L74】【F:Veriado.Infrastructure/Integrity/IndexAuditor.cs†L1-L212】
- **Výkonnostní testy.** V adresáři `tools` je k dispozici skript `fts5-benchmark.csx`, který dokáže změřit propustnost dávkové indexace (v rámci transakce se provede rollback, aby se neovlivnila produkční data) a latence vybraných dotazů (50./95. percentil). Spouští se příkazem `dotnet script tools/fts5-benchmark.csx -- <cesta_k_db> "dotaz1" "dotaz2"`.【F:tools/fts5-benchmark.csx†L1-L167】
- **Známá omezení.** Backend nepodporuje fuzzy odpovídání ani trigramové shinglování. Veškeré vyhodnocení spoléhá na tokenizaci `unicode61` a případná tolerance musí být implementována na úrovni dotazu (např. pomocí synonym nebo prefixů). Highlighting je založen na funkcích `snippet()`/`offsets()` a nepoužívá HTML tagy – jejich doplnění je možné až na úrovni prezentační vrstvy.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L360-L520】

## Implementační scénáře
- **Právní a poradenské týmy** – Rychlý přístup k dokumentaci případů, smlouvám a důkazním materiálům.
- **Výzkumná a vývojová oddělení** – Sdílení technické dokumentace, experimentálních reportů a laboratorních deníků.
- **Back-office a finance** – Centralizace faktur, objednávek a smluv se zajištěnou auditní stopou.
- **Projektoví manažeři** – Jednotné úložiště projektových artefaktů s možností sledovat stav a platnost dokumentů.

## Potenciál dalšího rozvoje
- **Integrace s cloudovými službami** – Napojení na SharePoint, OneDrive či intranetové portály pro hybridní scénáře.
- **Pokročilá analytika** – Rozšíření o reporting, automatické značkování obsahu a doporučení souvisejících dokumentů.
- **Automatizované workflow** – Spouštění schvalovacích procesů, notifikací a připomínek navázaných na životní cyklus dokumentů.

## FAQ
**Jak rychle mohu začít?**
Stačí nainstalovat Windows App SDK, spustit instalaci Veriada a provést první import. Průvodce nastavením provede uživatele konfigurací úložiště a indexu.

**Je možné pracovat offline?**
Ano. Veškerá data, indexy i náhledy zůstávají lokálně, takže aplikace funguje plnohodnotně bez připojení k internetu.

**Jaké jsou hardwarové požadavky?**
Doporučujeme moderní čtyřjádrový procesor, 8 GB RAM a SSD. Vyšší výkon zrychluje dávkové importy a generování náhledů.

**Jak se řeší bezpečnost?**
Data jsou ukládána do chráněné databáze v uživatelském profilu. Nastavení práv přístupu a správa režimu jen pro čtení zajišťují, že citlivé informace zůstávají pod kontrolou.

## Začněte ještě dnes
Veriado přináší profesionální správu dokumentů do každodenní praxe. Uspořádejte svá data, zvyšte produktivitu a zajistěte, že se firemní znalosti neztratí v chaosu sdílených složek. Nainstalujte Veriado a objevte, jak snadné může být mít všechny klíčové informace vždy na dosah.
