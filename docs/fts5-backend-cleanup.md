# Návrhy na úklid backendu fulltextového vyhledávání

## 1. Odstranit zdrojové pole u `SearchHit`
* Kontrakty i doménový model stále vystavují vlastnost `Source` s komentářem odkazujícím na trigramový backend, který už v systému není.【F:Veriado.Contracts/Search/SearchHitDto.cs†L17-L33】【F:Veriado.Domain/Search/SearchHit.cs†L14-L46】
* Mapování hitů v `SqliteFts5QueryService` vždy vrací konstantní hodnotu `"FTS"`, jiný původ výsledků se nevytváří.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L280-L312】
* Doporučení: odstranit parametr `Source` z `SearchHit`/`SearchHitDto` a odpovídajících AutoMapper profilů. Zjednoduší se DTO, ušetří se serializace a eliminují se odkazy na již smazaný trigramový režim.
* Ve stejném kroku lze odstranit `SecondaryScore` z `SearchHitSortValues`. Sekundární skóre sloužilo ke slučování více backendů, ale aktuální implementace předává pouze FTS skóre.【F:Veriado.Domain/Search/SearchHit.cs†L35-L46】【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L302-L312】

## 2. Sloučit `HybridSearchQueryService` přímo do FTS vrstvy
* Třída `HybridSearchQueryService` je dnes jen tenká proxy, která přeposílá všechny požadavky do `SqliteFts5QueryService` a přidává měření latence.【F:Veriado.Infrastructure/Search/HybridSearchQueryService.cs†L11-L63】
* Samotný `SqliteFts5QueryService` už ale telemetrii FTS dotazů měří, takže proxy nepřináší další logiku.【F:Veriado.Infrastructure/Search/SqliteFts5QueryService.cs†L200-L225】
* Doporučení: implementovat `ISearchQueryService` přímo v `SqliteFts5QueryService` (nebo přejmenovat) a vyřadit `HybridSearchQueryService`. DI registrace (`ServiceCollectionExtensions`) se tím zjednoduší a odpadne zbytečné přepínání instancí.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L148-L154】

## 3. Pročistit telemetrické API
* Metoda `RecordSearchLatency` ve `SearchTelemetry` je používaná pouze z `HybridSearchQueryService`; po jeho odstranění zůstane mrtvá.【F:Veriado.Infrastructure/Search/SearchTelemetry.cs†L35-L87】【F:Veriado.Infrastructure/Search/HybridSearchQueryService.cs†L48-L62】
* Návrh: buď přenést měření celkové latence přímo do FTS služby (pokud má dále smysl), nebo metodu z rozhraní `ISearchTelemetry` i implementace zcela odebrat.

Tyto kroky odstraní zbytečné abstractions po bývalém hybridním/trigramovém režimu a zjednoduší údržbu FTS5 backendu.
