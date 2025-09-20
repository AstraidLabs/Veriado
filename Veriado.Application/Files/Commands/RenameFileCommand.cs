using System;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.Files.Commands;

/// <summary>
/// Represents a command describing a request to rename a file.
/// </summary>
/// <param name="FileId">The identifier of the file to rename.</param>
/// <param name="NewName">The new file name without extension.</param>
public sealed record RenameFileCommand(Guid FileId, FileName NewName);
