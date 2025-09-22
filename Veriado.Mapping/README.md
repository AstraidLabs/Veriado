# Veriado.Mapping

The mapping project encapsulates all AutoMapper profiles and anti-corruption components required to bridge the public DTO layer and the application/domain core.

## Composition

- `Profiles/` defines read projections (`FileReadProfiles`), write conversions (`FileWriteProfiles`) and metadata conversions (`MetadataProfiles`) shared across the solution.
- `AC/` contains anti-corruption helpers such as the `Parsers`, DTO guards and the `WriteMappingPipeline` orchestrating request-to-command mapping with validation.
- `DependencyInjection/MappingServiceCollectionExtensions` registers all mapping services, validators and the pipeline with `AssertConfigurationIsValid()` executed in DEBUG builds.

The project targets .NET 8, uses AutoMapper for transformations and FluentValidation for command validation.

## Inventory snapshot (analysis)

- AutoMapper profiles in this project cover domain aggregates, EF read models and search records → contract DTOs. `FileReadProfiles` handles summaries/detail projections, `MetadataProfiles` formats metadata, `FileWriteProfiles` maps DTO inputs to value objects and `SearchProfiles` hydrates search DTOs.
- Services and WinUI depend exclusively on `Veriado.Contracts` (including search history/favourite contracts). Application handlers consume `IFileReadRepository` read models and map them through `IMapper`.
- Query pipelines rely on AutoMapper for efficient materialisation (`ProjectTo<TDto>(_mapper.ConfigurationProvider)` in `FileGridQueryHandler`), while simpler list/detail handlers map read models post-query without exposing aggregates.

## Refactoring plan

1. Keep file write handlers delegating through `FileWriteHandlerBase` with `IMapper` supplied via DI so summary mapping remains centralised.
2. Maintain the mapping documentation here and ensure hosts register `AddVeriadoMapping()` before using services or WinUI components.
3. When new EF projections are introduced, express them via AutoMapper and favour `ProjectTo<TDto>(_mapper.ConfigurationProvider)` to avoid loading large payloads.

## Notes

- AutoMapper is the single mapping surface between the domain and public contracts; helper classes such as `DomainToDto` are no longer needed.
- UI/services operate on `Veriado.Contracts` DTOs wired through this project; DI registration asserts configuration validity in DEBUG builds.
- `FileGridQueryHandler` demonstrates how to combine filtering with `ProjectTo` to keep EF evaluation on the server side and skip BLOB columns.
