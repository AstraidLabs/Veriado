# Veriado.Mapping

The mapping project encapsulates all AutoMapper profiles and anti-corruption components required to bridge the public DTO layer and the application/domain core.

## Composition

- `Profiles/` defines read projections (`FileReadProfiles`), write conversions (`FileWriteProfiles`) and metadata conversions (`MetadataProfiles`) shared across the solution.
- `AC/` contains anti-corruption helpers such as the `Parsers`, DTO guards and the `WriteMappingPipeline` orchestrating request-to-command mapping with validation.
- `DependencyInjection/MappingServiceCollectionExtensions` registers all mapping services, validators and the pipeline with `AssertConfigurationIsValid()` executed in DEBUG builds.

The project targets .NET 8, uses AutoMapper for transformations and FluentValidation for command validation.
