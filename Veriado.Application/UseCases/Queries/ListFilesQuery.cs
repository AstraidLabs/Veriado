using MediatR;
using Veriado.Appl.Common;
using Veriado.Contracts.Files;

namespace Veriado.Appl.UseCases.Queries;

/// <summary>
/// Query to obtain a paginated list of files.
/// </summary>
/// <param name="PageNumber">The 1-based page number.</param>
/// <param name="PageSize">The size of the page.</param>
public sealed record ListFilesQuery(int PageNumber, int PageSize) : IRequest<Page<FileListItemDto>>;
