using System;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Raised when file content is replaced.
/// </summary>
public sealed record FileContentReplaced(
    Guid FileId,
    FileHash OldHash,
    FileHash NewHash,
    ByteSize OldSize,
    ByteSize NewSize,
    int NewVersion,
    DateTimeOffset OccurredUtc) : IDomainEvent;
