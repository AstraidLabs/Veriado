# Veriado.Application

## UseCases jako jediná API vrstva

Aplikační projekt vystavuje veřejné API výhradně prostřednictvím MediatR **UseCases**. Každý zápisový scénář má vlastní `IRequest` command a handler, na které je navázána validační pipeline (`LoggingBehavior`, `IdempotencyBehavior`, `ValidationBehavior`). Legacy typy pod `Veriado.Application.Files` byly odstraněny – použití `UseCases` je jediná podporovaná cesta.

| UseCase | Popis |
| --- | --- |
| `UseCases.Files.CreateFile.CreateFileCommand` | Vytvoření nového souboru včetně prvotního indexování. |
| `UseCases.Files.ReplaceFileContent.ReplaceFileContentCommand` | Náhrada binárního obsahu a přegenerování fulltextu. |
| `UseCases.Files.RenameFile.RenameFileCommand` | Přejmenování souboru a synchronizace indexu. |
| `UseCases.Files.UpdateFileMetadata.UpdateFileMetadataCommand` | Aktualizace MIME a autora. |
| `UseCases.Files.SetExtendedMetadata.SetExtendedMetadataCommand` | Hromadná správa rozšířené metadatové struktury. |
| `UseCases.Files.ApplySystemMetadata.ApplySystemMetadataCommand` | Import systémových metadat (atributy, časová razítka). |
| `UseCases.Files.SetFileReadOnly.SetFileReadOnlyCommand` | Přepnutí příznaku pouze pro čtení. |
| `UseCases.Files.SetFileValidity.SetFileValidityCommand` / `ClearFileValidityCommand` | Správa platnosti dokumentu. |
| `UseCases.Maintenance.ReindexFileCommand` / `BulkReindexCommand` / `VerifyAndRepairFulltextCommand` | Údržba a manuální reindexace. |

Každý command má odpovídající validátor v `UseCases.Files.Validation`, který dědí z `FluentValidation.AbstractValidator` a zároveň implementuje `IRequestValidator<T>`, takže se automaticky zapojuje do pipeline. Spotřebitelé by měli pracovat výhradně s `IMediator.Send` a těmito UseCases.

Dotazovací část je sjednocena v `UseCases.Queries` – například `FileGridQueryHandler` využívá `QueryableFilters`, `FtsQueryBuilder` a `TrigramQueryBuilder` pro pokročilé filtrování, řazení a fulltextové vyhledávání.
