using System;
using Veriado.Domain.Primitives;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Raised when the read-only state of a file changes.
/// </summary>
public sealed record FileReadOnlyChanged(
    Guid FileId,
    bool IsReadOnly,
    DateTimeOffset OccurredUtc) : IDomainEvent;
