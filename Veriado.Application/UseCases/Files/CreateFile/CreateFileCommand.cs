using System;
using MediatR;
using Veriado.Appl.Common;

namespace Veriado.Appl.UseCases.Files.CreateFile;

/// <summary>
/// Command used to create a new file aggregate with content.
/// </summary>
/// <param name="Name">The file name without extension.</param>
/// <param name="Extension">The file extension.</param>
/// <param name="Mime">The MIME type.</param>
/// <param name="Author">The document author.</param>
/// <param name="Content">The binary content of the file.</param>
public sealed record CreateFileCommand(
    string Name,
    string Extension,
    string Mime,
    string Author,
    byte[] Content) : IRequest<AppResult<Guid>>;
