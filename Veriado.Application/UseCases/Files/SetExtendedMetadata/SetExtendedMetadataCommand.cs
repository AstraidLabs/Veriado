using System;
using System.Collections.Generic;
using MediatR;
using Veriado.Application.Common;
using Veriado.Application.DTO;

namespace Veriado.Application.UseCases.Files.SetExtendedMetadata;

/// <summary>
/// Command to set extended metadata values on a file.
/// </summary>
/// <param name="FileId">The file identifier.</param>
/// <param name="Entries">The metadata entries to upsert or remove.</param>
public sealed record SetExtendedMetadataCommand(Guid FileId, IReadOnlyCollection<ExtendedMetadataEntry> Entries) : IRequest<AppResult<FileDto>>;

/// <summary>
/// Represents a single extended metadata operation.
/// </summary>
/// <param name="FormatId">The property set format identifier.</param>
/// <param name="PropertyId">The property identifier.</param>
/// <param name="Value">The value to set, or <see langword="null"/> to remove.</param>
public sealed record ExtendedMetadataEntry(Guid FormatId, int PropertyId, string? Value);
