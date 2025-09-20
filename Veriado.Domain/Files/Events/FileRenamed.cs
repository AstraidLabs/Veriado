using System;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Raised when the file name or extension changes.
/// </summary>
public sealed record FileRenamed(
    Guid FileId,
    FileName OldName,
    FileExtension OldExtension,
    FileName NewName,
    FileExtension NewExtension,
    DateTimeOffset OccurredUtc) : IDomainEvent;
