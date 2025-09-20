using System;

namespace Veriado.Application.Files.Commands;

/// <summary>
/// Represents a command describing the removal of validity information from a file.
/// </summary>
/// <param name="FileId">The identifier of the file to update.</param>
public sealed record ClearValidityCommand(Guid FileId);
