# Veriado completion analysis

## Critical missing features
- **File deletion workflow is absent.** The application exposes create/update operations only; there is no `DeleteFile` use case, validator or service even though the repository supports removals. Completing the product requires a MediatR command, UI action and API plumbing so users can purge files when needed.【F:Veriado.Application/README.md†L7-L18】【F:Veriado.Services/Files/FileOperationsService.cs†L17-L155】【F:Veriado.Infrastructure/Repositories/FileRepository.cs†L49-L92】
- **Binary content never reaches the full-text index.** Infrastructure documentation confirms that only titles and metadata are indexed; without text extraction (PDF/Office parsers, OCR hooks) search cannot find document body text. Shipping the app demands a content-extraction pipeline and storage format for extracted text and snippets.【F:Veriado.Infrastructure/README.md†L1-L4】【F:Veriado.Domain/Files/FileEntity.cs†L312-L361】

## WinUI experience gaps
- **Search box ignores history/favourite suggestions.** The UI AutoSuggestBox binds only to `SearchText` and never loads suggestions, despite a provider that merges favourites and history. Hook up `ItemsSource`, `TextChanged` and `SuggestionChosen` handlers so saved queries surface while typing.【F:Veriado.WinUI/Views/Files/FilesPage.xaml†L33-L49】【F:Veriado.WinUI/Services/FilesSearchSuggestionsProvider.cs†L6-L44】
- **Import wizard hides search-pattern controls.** `ImportOptions` supports custom glob patterns, but `ImportPageViewModel` hardcodes `"*"`, preventing users from narrowing imports (e.g., `*.pdf`). Add UI fields, validation and persistence for the pattern to make selective import usable.【F:Veriado.WinUI/ViewModels/Import/ImportPageViewModel.cs†L480-L507】【F:Veriado.Services/Import/ImportService.cs†L180-L215】
- **Preview pane lacks rich rendering.** The preview service only shows a UTF-8 snippet for textual MIME types and emoji fallbacks for everything else. To match the product vision, integrate real renderers (PDF to image, Office converters, image thumbnails) and cache management for large previews.【F:Veriado.WinUI/Services/PreviewService.cs†L7-L134】

## Platform & release work
- **Packaging metadata still uses placeholders.** The AppX manifest keeps the default GUID, publisher `CN=tomas` and generic assets. Before release you need a production identity, signing certificate, tile assets and automated MSIX packaging/signing steps.【F:Veriado.WinUI/Package.appxmanifest†L3-L50】
- **Data indexing health must cover binary ingestion.** Once text extraction exists, extend health diagnostics so `GetHealthStatusHandler` reports extractor/backlog issues, and surface warnings in the Files dashboard alongside current indexing alerts.【F:Veriado.Application/UseCases/Diagnostics/GetHealthStatusHandler.cs†L1-L200】【F:Veriado.WinUI/ViewModels/Files/FilesPageViewModel.cs†L19-L129】

## Quality & automation
- **Test coverage focuses solely on query builders.** The test project pouze prověřuje FTS query builder; chybí unit/integration testy pro importy, mutace souborů, UI view-modely či infrastrukturu. Rozšiřte testy (doménové invariants, streamované importy, WinUI view-modely) a doplňte end-to-end smoke scénáře.【F:Veriado.Application.Tests/Search/FtsQueryBuilderTests.cs†L1-L64】
- **Continuous integration is missing.** Add pipelines that build all projects, run tests, lint XAML, and produce signed MSIX artifacts. Include SQLite migration validation and full-text index smoke checks to catch regressions automatically.【F:README.md†L1-L68】

## Configuration & operations
- **Storage location is hardcoded.** `InfrastructureConfigProvider` always provisions `%LocalAppData%\Veriado\veriado.db`, leaving no way to choose another drive or network share. Provide settings/first-run wizard to change the storage root and migrate data between locations.【F:Veriado.WinUI/Services/InfrastructureConfigProvider.cs†L6-L24】
- **Synonym/facet management lacks tooling.** Backend services expose synonyms, facets and suggestions, but the WinUI layer has no configuration UI. Deliver management screens so administrators can edit synonym tables, inspect facets and prune suggestion dictionaries without direct SQLite access.【F:Veriado.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs†L93-L166】【F:Veriado.WinUI/Views/Files/FilesPage.xaml†L33-L160】
