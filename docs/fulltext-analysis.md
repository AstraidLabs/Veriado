# Analýza fulltextového vyhledávání

> **Poznámka:** Funkce fuzzy vyhledávání a trigramové dotazy byly odstraněny. Následující informace popisují historickou architekturu a slouží pouze jako referenční dokumentace.

## Architektura a tok dat
- **FTS5 vrstva.** Schéma SQLite vytváří virtuální tabulky `file_search` (title, mime, author, metadata_text, metadata) a `file_trgm` (trigramy) doplněné o mapovací tabulky pro převod `rowid` ↔︎ `file_id`. Tokenizer `unicode61` běží bez diakritiky a s prázdným `content`, takže aplikace kompletně řídí synchronizaci dat.【F:Veriado.Infrastructure/Persistence/Schema/Fts5.sql†L1-L30】
- **Pipeline indexace.** `FileEntity.ToSearchDocument()` promění doménový agregát na `SearchDocument`, který kromě serializovaného JSONu nese i předrenderovaný textový souhrn metadat. `SqliteFts5Transactional` zapisuje titul, autora, MIME, `metadata_text` i JSON a paralelně buduje trigramy. Celá operace probíhá synchronně v rámci jedné SQLite transakce – žádná odkládací fronta (outbox) se již nepoužívá.【F:Veriado.Domain/Files/FileEntity.cs†L365-L396】【F:Veriado.Domain/Search/SearchDocument.cs†L11-L58】【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L10-L187】
- **Koordinace zápisů.** `SqliteSearchIndexCoordinator` rozhoduje, zda se indexace provede okamžitě v probíhající transakci, nebo se odloží. Při režimu `SameTransaction` využívá pomocné třídy `SqliteFts5Transactional`, v ostatních případech deleguje na singleton `SqliteFts5Indexer`, který změny provede v samostatném připojení.【F:Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs†L9-L74】【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L7-L88】
- **Fuzzy vs. přesné dotazy.** Trigram index se nyní omezuje na human-readable pole (titul, autor, název souboru, `metadata_text`). Přesné FTS dotazy používají `bm25` s váhami titul 4.0, autor 2.0, `metadata_text` 0.8, `metadata` 0.2 a MIME 0.1; fuzzy dotazy přepínají na trigramy a následné řazení podle skóre z FTS5.【F:Veriado.Application/Search/TrigramQueryBuilder.cs†L1-L156】【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L23-L229】

## Pokrytí indexovaných polí
- **FTS5 (přesné vyhledávání).** Do tabulky `file_search` se ukládá titul (`title`), MIME (`mime`), autor (`author`), předrenderovaný souhrn (`metadata_text`) a zpětně kompatibilní JSON (`metadata`). `metadata_text` slouží pro snippet i vážení, zatímco JSON se využívá pouze při fallbacku a pro zachování kompatibility se staršími indexy.【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L32-L70】【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L170-L321】
- **Trigram (fuzzy vyhledávání).** Fuzzy index zahrnuje titul, autora, název souboru a `metadata_text`. Strojové JSON hodnoty se do trigramů nepromítají, čímž se dramaticky snižuje šum i velikost indexu.【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L59-L70】【F:Veriado.Application/Search/TrigramQueryBuilder.cs†L33-L108】
- **FTS5 dotazy.** Vyhledávání vrací `SearchHit` s váženým skóre z `bm25` a fragmentem ze sloupce `metadata_text`. Zvýraznění fragmentu zajišťuje SQL funkce `snippet`, která respektuje váhy jednotlivých sloupců a preferuje titul/autora.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L77-L229】

## Kritické nedostatky
1. **Kvalita `metadata_text` závisí na zdrojových datech.** Souhrn využívá dostupné vlastnosti (vlastník, atributy, velikost). Pokud metadata neobsahují překlad SID → jméno, zůstává strohé SID. Zvažujeme doplnění lokální cache SID → display name z adresářových služeb.【F:Veriado.Domain/Search/MetadataTextFormatter.cs†L12-L104】
## Doporučená vylepšení
- **Omezení velikosti trigram indexu.** `TrigramQueryBuilder.BuildIndexEntry` nyní při indexaci zastaví přidávání tokenů po dosažení 2 048 trigramů, čímž brání exponenciálnímu růstu indexu u dokumentů s extrémně dlouhým obsahem. Při budoucích úpravách je vhodné tento limit sledovat a případně parametrizovat podle potřeb nasazení.【F:Veriado.Application/Search/TrigramQueryBuilder.cs†L5-L115】【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L48-L69】
- **Zpřesnit enriched metadata.** Současný souhrn zahrnuje velikost, vlastníka, atributy, ADS/Hlink. Do budoucna lze zvážit překlad SID → jméno uživatele nebo doplnění lokalizovaných názvů atributů.【F:Veriado.Domain/Search/MetadataTextFormatter.cs†L12-L104】
- **Rozšířit zdroje pro `metadata_text`.** Pokud budou přibývat uživatelské tagy nebo klasifikace, vyplatí se přidat další lidsky čitelné části do souhrnu, aby uživatelé viděli kontext bez otevírání detailu.【F:Veriado.Domain/Search/SearchDocument.cs†L11-L58】
- **Monitorovat dopad vážení.** V případě nových polí nebo změn v doméně je vhodné průběžně sledovat distribuci skóre a případně upravit váhy `bm25`, aby výsledky zachovaly preferenci titulů a autorů.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L23-L229】

## Facety, návrhy a kontrola pravopisu

Nová infrastruktura nad SQLite poskytuje trojici rozhraní pro facety, návrhy a přibližné opravy:

```csharp
var facets = await facetService.GetFacetsAsync(
    new FacetRequest(new[]
    {
        new FacetField("mime", FacetKind.Term),
        new FacetField("modified", FacetKind.DateHistogram, interval: "month"),
        new FacetField("size", FacetKind.NumericRange),
    }),
    cancellationToken);

var suggestions = await suggestionService.SuggestAsync("quar", language: "en", limit: 5, cancellationToken);
var corrections = await spellSuggestionService.SuggestAsync("reciept", "en", limit: 3, threshold: 0.35, cancellationToken);
```

Synonyma se spravují přes tabulku `synonyms` a `SearchQueryBuilder` je automaticky rozšiřuje během kompilace dotazu:

```sql
INSERT INTO synonyms(lang, term, variant) VALUES ('en', 'invoice', 'bill');
INSERT INTO synonyms(lang, term, variant) VALUES ('en', 'analysis', 'analytics');
```

Tabulka `suggestions` je napájena službou `SuggestionMaintenanceService`, která při indexaci sklízí tokeny z názvů, autora a metadat a zapisuje je s váhami. Stejná data slouží jako slovník pro trigramovou kontrolu pravopisu.

## Konfigurace a DI registrace

- `ServiceCollectionExtensions` registruje všechny dílčí možnosti (`SearchOptions.Score`, `Trigram`, `Facets`, `Synonyms`, `Suggestions`, `Spell`) a zároveň publikuje `IOptions<T>` pro analyzér i trigramový index. Díky tomu lze nastavení přirozeně injektovat do `HybridSearchQueryService`, `SqliteFts5Transactional` a dalších služeb.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L93-L123】
- `SearchOptions` agregují výchozí hodnoty – například prefixová váha titulu 4.0, limit trigramů 2 048 tokenů a seznam indexovaných polí (`title`, `author`, `filename`, `metadata_text`).【F:Veriado.Application/Search/SearchOptions.cs†L7-L106】
- `SqliteFts5Transactional` respektuje nakonfigurovaný seznam polí i limit tokenů při generování trigramového obsahu; přebytek ořeže ještě před vložením do tabulky `file_trgm`.【F:Veriado.Infrastructure/Search/SqliteFts5Transactional.cs†L18-L127】

Příklad úseku `appsettings.json` v produkci:

```json
{
  "Search": {
    "Analyzer": {
      "DefaultProfile": "cs"
    },
    "Score": {
      "OversampleMultiplier": 3,
      "DefaultTrigramScale": 0.45,
      "TrigramFloor": 0.3,
      "MergeMode": "weighted",
      "WeightedFts": 0.7
    },
    "Trigram": {
      "MaxTokens": 2048,
      "Fields": ["title", "author", "filename", "metadata_text"]
    }
  }
}
```

## Příklady integrace

1. **Kombinovaný boolovský + frázový + rozsahový dotaz přes EF Core**

    ```csharp
    var builder = new SearchQueryBuilder(options.Score, synonymProvider, options.Analyzer.DefaultProfile);
    var query = builder.And(
        builder.Term("metadata_text", "faktura"),
        builder.Phrase("title", "daňový doklad"),
        builder.Range("modified_utc", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero)));
    var plan = builder.Build(query, rawQuery: "faktura title:\"daňový doklad\" modified:[2024-01-01 TO 2024-12-31]");
    var hits = await searchQueryService.SearchAsync(plan, limit: 50, cancellationToken);
    ```

2. **Prefixový dotaz s fallbackem na trigramy (`ucte*`)**

    ```csharp
    var builder = new SearchQueryBuilder(options.Score, synonymProvider, options.Analyzer.DefaultProfile);
    var prefix = builder.Prefix("metadata_text", "ucte");
    var plan = builder.Build(prefix, rawQuery: "ucte*");
    var results = await searchQueryService.SearchAsync(plan, limit: 20, cancellationToken);
    // plan.RequiresTrigramFallback == true → HybridSearchQueryService automaticky kombinuje FTS5 i trigramové skóre.
    ```

3. **Fuzzy překlep `uctenk` → trigramový Jaccard + normalizované FTS skóre**

    ```csharp
    var fuzzy = builder.Term(null, "uctenk");
    var fuzzyPlan = builder.Build(fuzzy, rawQuery: "uctenk");
    var fuzzyHits = await searchQueryService.SearchAsync(fuzzyPlan, limit: 10, cancellationToken);
    // HybridSearchQueryService využije _scoreOptions.DefaultTrigramScale a TrigramFloor, aby výsledky z trigramů držely konzistentní pořadí.【F:Veriado.Infrastructure/Search/HybridSearchQueryService.cs†L26-L155】
    ```

4. **Vykreslení zvýraznění ve WinUI**

    ```csharp
    foreach (var hit in hits)
    {
        var spans = hit.Highlights.Where(span => span.Field == hit.PrimaryField);
        var textBlock = new TextBlock { Text = hit.SnippetText };
        foreach (var span in spans)
        {
            textBlock.TextHighlighters.Add(new TextHighlighter
            {
                Ranges = { new TextRange(span.Start, span.Length) },
                Background = highlightBrush
            });
        }
        resultsPanel.Children.Add(textBlock);
    }
    ```

5. **Volání facetování nad MIME a měsíční histogram**

    ```csharp
    var request = new FacetRequest(new[]
    {
        new FacetField("mime", FacetKind.Term),
        new FacetField("modified_utc", FacetKind.DateHistogram, interval: "month"),
    });
    var facetResult = await facetService.GetFacetsAsync(request, cancellationToken);
    ```
