using System;
using Veriado.Domain.Primitives;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Raised when validity information is added, updated, or removed.
/// </summary>
public sealed record FileValidityChanged(
    Guid FileId,
    DateTimeOffset OccurredUtc,
    DateTimeOffset? IssuedAtUtc,
    DateTimeOffset? ValidUntilUtc,
    bool HasPhysicalCopy,
    bool HasElectronicCopy) : IDomainEvent;
