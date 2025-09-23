using MediatR;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;

namespace Veriado.Appl.UseCases.Queries.FileGrid;

/// <summary>
/// Represents the advanced grid query over files.
/// </summary>
/// <param name="Parameters">The query parameters.</param>
public sealed record FileGridQuery(FileGridQueryDto Parameters) : IRequest<PageResult<FileSummaryDto>>;
