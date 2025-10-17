using Veriado.Domain.FileSystem.Events;
using Veriado.Domain.Files.Events;
using Veriado.Domain.Primitives;
using Veriado.Infrastructure.Persistence.Audit;

namespace Veriado.Infrastructure.Events;

/// <summary>
/// Projects domain events emitted during write transactions into audit tables.
/// </summary>
internal sealed class AuditEventProjector
{
    private readonly ILogger<AuditEventProjector> _logger;

    public AuditEventProjector(ILogger<AuditEventProjector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Projects the supplied domain events into the audit tables tracked by the provided context.
    /// </summary>
    /// <param name="context">The active write context.</param>
    /// <param name="domainEvents">The domain events to project.</param>
    /// <returns><c>true</c> if any audit entries were added; otherwise <c>false</c>.</returns>
    public bool Project(AppDbContext context, IReadOnlyList<(Guid AggregateId, IDomainEvent DomainEvent)> domainEvents)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(domainEvents);

        if (domainEvents.Count == 0)
        {
            return false;
        }

        var hasChanges = false;

        foreach (var (_, domainEvent) in domainEvents)
        {
            switch (domainEvent)
            {
                case FileCreated created:
                    context.FileAudits.Add(new FileAuditRecord
                    {
                        FileId = created.FileId,
                        Action = FileAuditAction.Created,
                        Description = $"Created as '{created.Name.Value}'",
                        Mime = created.Mime.Value,
                        Author = created.Author,
                        OccurredUtc = UtcTimestamp.From(created.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileRenamed renamed:
                    context.FileAudits.Add(new FileAuditRecord
                    {
                        FileId = renamed.FileId,
                        Action = FileAuditAction.Renamed,
                        Description = $"Renamed from '{renamed.OldName.Value}' to '{renamed.NewName.Value}'",
                        OccurredUtc = UtcTimestamp.From(renamed.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileMetadataUpdated metadataUpdated:
                    context.FileAudits.Add(new FileAuditRecord
                    {
                        FileId = metadataUpdated.FileId,
                        Action = FileAuditAction.MetadataUpdated,
                        Description = $"Metadata updated (MIME: {metadataUpdated.Mime.Value}, Author: {metadataUpdated.Author}, Title: {(string.IsNullOrWhiteSpace(metadataUpdated.Title) ? "(none)" : $"'{metadataUpdated.Title}'")})",
                        Mime = metadataUpdated.Mime.Value,
                        Author = metadataUpdated.Author,
                        Title = string.IsNullOrWhiteSpace(metadataUpdated.Title) ? null : metadataUpdated.Title,
                        OccurredUtc = UtcTimestamp.From(metadataUpdated.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileReadOnlyChanged readOnlyChanged:
                    context.FileAudits.Add(new FileAuditRecord
                    {
                        FileId = readOnlyChanged.FileId,
                        Action = FileAuditAction.ReadOnlyChanged,
                        Description = $"Read-only {(readOnlyChanged.IsReadOnly ? "enabled" : "disabled")}",
                        OccurredUtc = UtcTimestamp.From(readOnlyChanged.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileValidityChanged validityChanged:
                    context.FileAudits.Add(new FileAuditRecord
                    {
                        FileId = validityChanged.FileId,
                        Action = FileAuditAction.ValidityChanged,
                        Description = $"Validity updated (Issued: {FormatTimestamp(validityChanged.IssuedAt)}, Expires: {FormatTimestamp(validityChanged.ValidUntil)}, Physical: {validityChanged.HasPhysicalCopy}, Electronic: {validityChanged.HasElectronicCopy})",
                        OccurredUtc = UtcTimestamp.From(validityChanged.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileContentLinked contentLinked:
                    context.FileLinkAudits.Add(new FileLinkAuditRecord
                    {
                        FileId = contentLinked.FileId,
                        FileSystemId = contentLinked.FileSystemId,
                        Action = FileLinkAuditAction.Linked,
                        Version = contentLinked.Version.Value,
                        Hash = contentLinked.Hash.Value,
                        Size = contentLinked.Size.Value,
                        Mime = contentLinked.Mime.Value,
                        OccurredUtc = UtcTimestamp.From(contentLinked.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileContentRelinked contentRelinked:
                    context.FileLinkAudits.Add(new FileLinkAuditRecord
                    {
                        FileId = contentRelinked.FileId,
                        FileSystemId = contentRelinked.FileSystemId,
                        Action = FileLinkAuditAction.Relinked,
                        Version = contentRelinked.Version.Value,
                        Hash = contentRelinked.Hash.Value,
                        Size = contentRelinked.Size.Value,
                        Mime = contentRelinked.Mime.Value,
                        OccurredUtc = UtcTimestamp.From(contentRelinked.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileSystemContentChanged systemContentChanged:
                    context.FileSystemAudits.Add(new FileSystemAuditRecord
                    {
                        FileSystemId = systemContentChanged.FileSystemId,
                        Action = FileSystemAuditAction.ContentChanged,
                        Path = systemContentChanged.Path.Value,
                        Hash = systemContentChanged.Hash.Value,
                        Size = systemContentChanged.Size.Value,
                        Mime = systemContentChanged.Mime.Value,
                        IsEncrypted = systemContentChanged.IsEncrypted,
                        OccurredUtc = UtcTimestamp.From(systemContentChanged.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileSystemMoved systemMoved:
                    context.FileSystemAudits.Add(new FileSystemAuditRecord
                    {
                        FileSystemId = systemMoved.FileSystemId,
                        Action = FileSystemAuditAction.Moved,
                        Path = systemMoved.NewPath.Value,
                        OccurredUtc = UtcTimestamp.From(systemMoved.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileSystemAttributesChanged attributesChanged:
                    context.FileSystemAudits.Add(new FileSystemAuditRecord
                    {
                        FileSystemId = attributesChanged.FileSystemId,
                        Action = FileSystemAuditAction.AttributesChanged,
                        Attributes = (int)attributesChanged.Attributes,
                        OccurredUtc = UtcTimestamp.From(attributesChanged.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileSystemOwnerChanged ownerChanged:
                    context.FileSystemAudits.Add(new FileSystemAuditRecord
                    {
                        FileSystemId = ownerChanged.FileSystemId,
                        Action = FileSystemAuditAction.OwnerChanged,
                        OwnerSid = ownerChanged.OwnerSid,
                        OccurredUtc = UtcTimestamp.From(ownerChanged.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileSystemTimestampsUpdated timestampsUpdated:
                    context.FileSystemAudits.Add(new FileSystemAuditRecord
                    {
                        FileSystemId = timestampsUpdated.FileSystemId,
                        Action = FileSystemAuditAction.TimestampsUpdated,
                        OccurredUtc = UtcTimestamp.From(timestampsUpdated.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileSystemMissingDetected missingDetected:
                    context.FileSystemAudits.Add(new FileSystemAuditRecord
                    {
                        FileSystemId = missingDetected.FileSystemId,
                        Action = FileSystemAuditAction.MissingDetected,
                        Path = missingDetected.Path.Value,
                        OccurredUtc = UtcTimestamp.From(missingDetected.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                case FileSystemRehydrated rehydrated:
                    context.FileSystemAudits.Add(new FileSystemAuditRecord
                    {
                        FileSystemId = rehydrated.FileSystemId,
                        Action = FileSystemAuditAction.Rehydrated,
                        Path = rehydrated.Path.Value,
                        OccurredUtc = UtcTimestamp.From(rehydrated.OccurredOnUtc),
                    });
                    hasChanges = true;
                    break;

                default:
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("No audit projection configured for domain event {EventType}", domainEvent.GetType().Name);
                    }

                    break;
            }
        }

        return hasChanges;
    }

    private static string FormatTimestamp(UtcTimestamp? timestamp)
    {
        return timestamp.HasValue ? timestamp.Value.ToString() : "(none)";
    }
}
