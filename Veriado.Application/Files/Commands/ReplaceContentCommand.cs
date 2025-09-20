using System;

namespace Veriado.Application.Files.Commands;

/// <summary>
/// Represents a command describing a request to replace the binary content of a file.
/// </summary>
/// <param name="FileId">The identifier of the file to update.</param>
/// <param name="ContentBytes">The new binary content.</param>
/// <param name="MaxContentLength">An optional maximum content length constraint.</param>
public sealed record ReplaceContentCommand(Guid FileId, byte[] ContentBytes, int? MaxContentLength);
