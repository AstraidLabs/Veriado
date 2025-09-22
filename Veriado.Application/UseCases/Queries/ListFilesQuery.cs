using MediatR;
using Veriado.Application.Common;
using Veriado.Contracts.Files;

namespace Veriado.Application.UseCases.Queries;

/// <summary>
/// Query to obtain a paginated list of files.
/// </summary>
/// <param name="PageNumber">The 1-based page number.</param>
/// <param name="PageSize">The size of the page.</param>
public sealed record ListFilesQuery(int PageNumber, int PageSize) : IRequest<Page<FileListItemDto>>;
