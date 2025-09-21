# Veriado.Application

## UseCases jako jediná API vrstva

Aplikační projekt vystavuje veřejné API výhradně prostřednictvím MediatR **UseCases**. Každý zápisový scénář má vlastní `IRequest` command a handler, na které je navázána validační pipeline (`LoggingBehavior`, `IdempotencyBehavior`, `ValidationBehavior`). Staré typy pod `Veriado.Application.Files` jsou pouze pro zpětnou kompatibilitu a jsou označeny jako `Obsolete`.

| Legacy API (`Veriado.Application.Files`)| Náhrada v UseCases |
| --- | --- |
| `CreateFileCommand` + `CreateFileHandler` | `UseCases.Files.CreateFile.CreateFileCommand` + `CreateFileHandler` |
| `ReplaceContentCommand` + `ReplaceContentHandler` | `UseCases.Files.ReplaceFileContent.ReplaceFileContentCommand` + `ReplaceFileContentHandler` |
| `RenameFileCommand` + `RenameFileHandler` | `UseCases.Files.RenameFile.RenameFileCommand` + `RenameFileHandler` |
| `UpdateMetadataCommand` | `UseCases.Files.UpdateFileMetadata.UpdateFileMetadataCommand` (+ případně `ApplySystemMetadataCommand`, `SetExtendedMetadataCommand`, `SetFileReadOnlyCommand`) |
| `SetValidityCommand` + `SetValidityHandler` | `UseCases.Files.SetFileValidity.SetFileValidityCommand` + `SetFileValidityHandler` |
| `ClearValidityCommand` + `ClearValidityHandler` | `UseCases.Files.ClearFileValidity.ClearFileValidityCommand` + `ClearFileValidityHandler` |
| `MetadataPatch` | `UseCases.Files.SetExtendedMetadata.SetExtendedMetadataCommand.ExtendedMetadataEntry` |
| Ruční reindexace (`FileIndexingHelper`, `Reindex` pomocníci) | `UseCases.Maintenance.ReindexFileCommand` / `BulkReindexCommand` / `VerifyAndRepairFulltextCommand` |

Každý nový command má odpovídající validátor v `UseCases.Files.Validation`, který dědí z `FluentValidation.AbstractValidator` a zároveň implementuje `IRequestValidator<T>`, takže se automaticky zapojuje do pipeline. Spotřebitelé by měli pracovat výhradně s `IMediator.Send` a těmito UseCases.

Dotazovací část (`Files.Queries`, `UseCases.Queries`) zůstává beze změn; `FileGridQueryHandler` dále využívá `QueryableFilters`, `FtsQueryBuilder` a `TrigramQueryBuilder`.
