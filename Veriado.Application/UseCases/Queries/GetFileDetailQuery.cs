namespace Veriado.Appl.UseCases.Queries;

/// <summary>
/// Query to retrieve a detailed representation of a file.
/// </summary>
/// <param name="FileId">The file identifier.</param>
public sealed record GetFileDetailQuery(Guid FileId) : IRequest<FileDetailDto?>;
