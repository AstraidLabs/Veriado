using MediatR;
using Veriado.Appl.Common;
using Veriado.Contracts.Files;

namespace Veriado.Appl.UseCases.Files.UpdateFileContent;

/// <summary>
/// Command used to refresh the physical content of a file from a user-supplied path.
/// </summary>
/// <param name="FileId">The identifier of the file to update.</param>
/// <param name="SourceFileFullPath">The full path of the new content on disk.</param>
public sealed record UpdateFileContentCommand(Guid FileId, string SourceFileFullPath) : IRequest<AppResult<FileSummaryDto>>;
