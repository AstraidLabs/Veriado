using System;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Raised when a new file aggregate is created.
/// </summary>
public sealed record FileCreated(
    Guid FileId,
    FileName Name,
    FileExtension Extension,
    MimeType MimeType,
    string Author,
    FileHash ContentHash,
    ByteSize Size,
    DateTimeOffset CreatedUtc) : IDomainEvent;
