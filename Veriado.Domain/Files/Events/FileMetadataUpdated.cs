using System;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files.Events;

/// <summary>
/// Raised when file metadata (MIME type or author) changes.
/// </summary>
public sealed record FileMetadataUpdated(
    Guid FileId,
    MimeType OldMimeType,
    MimeType NewMimeType,
    string OldAuthor,
    string NewAuthor,
    DateTimeOffset OccurredUtc) : IDomainEvent;
