using Veriado.Domain.Audit;
using Veriado.Domain.FileSystem.Events;
using Veriado.Domain.Files.Events;
using Veriado.Domain.Primitives;

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
                    context.FileAudits.Add(FileAuditEntity.Created(
                        created.FileId,
                        created.Name,
                        created.Mime,
                        created.Author,
                        UtcTimestamp.From(created.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileRenamed renamed:
                    context.FileAudits.Add(FileAuditEntity.Renamed(
                        renamed.FileId,
                        renamed.OldName,
                        renamed.NewName,
                        UtcTimestamp.From(renamed.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileMetadataUpdated metadataUpdated:
                    context.FileAudits.Add(FileAuditEntity.MetadataUpdated(
                        metadataUpdated.FileId,
                        metadataUpdated.Mime,
                        metadataUpdated.Author,
                        metadataUpdated.Title,
                        UtcTimestamp.From(metadataUpdated.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileReadOnlyChanged readOnlyChanged:
                    context.FileAudits.Add(FileAuditEntity.ReadOnlyChanged(
                        readOnlyChanged.FileId,
                        readOnlyChanged.IsReadOnly,
                        UtcTimestamp.From(readOnlyChanged.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileValidityChanged validityChanged:
                    context.FileAudits.Add(FileAuditEntity.ValidityChanged(
                        validityChanged.FileId,
                        validityChanged.IssuedAt,
                        validityChanged.ValidUntil,
                        validityChanged.HasPhysicalCopy,
                        validityChanged.HasElectronicCopy,
                        UtcTimestamp.From(validityChanged.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileContentLinked contentLinked:
                    context.FileLinkAudits.Add(FileLinkAuditEntity.Linked(
                        contentLinked.FileId,
                        contentLinked.FileSystemId,
                        contentLinked.Version,
                        contentLinked.Hash,
                        contentLinked.Size,
                        contentLinked.Mime,
                        UtcTimestamp.From(contentLinked.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileContentRelinked contentRelinked:
                    context.FileLinkAudits.Add(FileLinkAuditEntity.Relinked(
                        contentRelinked.FileId,
                        contentRelinked.FileSystemId,
                        contentRelinked.Version,
                        contentRelinked.Hash,
                        contentRelinked.Size,
                        contentRelinked.Mime,
                        UtcTimestamp.From(contentRelinked.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileSystemContentChanged systemContentChanged:
                    context.FileSystemAudits.Add(FileSystemAuditEntity.ContentChanged(
                        systemContentChanged.FileSystemId,
                        systemContentChanged.Path,
                        systemContentChanged.Hash,
                        systemContentChanged.Size,
                        systemContentChanged.Mime,
                        systemContentChanged.IsEncrypted,
                        UtcTimestamp.From(systemContentChanged.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileSystemMoved systemMoved:
                    context.FileSystemAudits.Add(FileSystemAuditEntity.Moved(
                        systemMoved.FileSystemId,
                        systemMoved.NewPath,
                        UtcTimestamp.From(systemMoved.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileSystemAttributesChanged attributesChanged:
                    context.FileSystemAudits.Add(FileSystemAuditEntity.AttributesChanged(
                        attributesChanged.FileSystemId,
                        attributesChanged.Attributes,
                        UtcTimestamp.From(attributesChanged.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileSystemOwnerChanged ownerChanged:
                    context.FileSystemAudits.Add(FileSystemAuditEntity.OwnerChanged(
                        ownerChanged.FileSystemId,
                        ownerChanged.OwnerSid,
                        UtcTimestamp.From(ownerChanged.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileSystemTimestampsUpdated timestampsUpdated:
                    context.FileSystemAudits.Add(FileSystemAuditEntity.TimestampsUpdated(
                        timestampsUpdated.FileSystemId,
                        UtcTimestamp.From(timestampsUpdated.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileSystemMissingDetected missingDetected:
                    context.FileSystemAudits.Add(FileSystemAuditEntity.MissingDetected(
                        missingDetected.FileSystemId,
                        missingDetected.Path,
                        UtcTimestamp.From(missingDetected.OccurredOnUtc)));
                    hasChanges = true;
                    break;

                case FileSystemRehydrated rehydrated:
                    context.FileSystemAudits.Add(FileSystemAuditEntity.Rehydrated(
                        rehydrated.FileSystemId,
                        rehydrated.Path,
                        UtcTimestamp.From(rehydrated.OccurredOnUtc)));
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
}
