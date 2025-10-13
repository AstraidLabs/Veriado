using System;
using System.Globalization;
using System.Text.Json;
using Veriado.Domain.Files.Events;
using Veriado.Domain.Metadata;
using Veriado.Domain.Search.Events;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence.Outbox;

namespace Veriado.Infrastructure.Events;

/// <summary>
/// Provides helpers to convert domain events to and from outbox payloads.
/// </summary>
internal static class OutboxDomainEventSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static bool TryCreateOutboxEvent(IDomainEvent domainEvent, DateTimeOffset createdUtc, out OutboxEventEntity? entity)
    {
        switch (domainEvent)
        {
            case FileCreated created:
                entity = CreateOutboxEntity(
                    nameof(FileCreated),
                    JsonSerializer.Serialize(new FileCreatedPayload(
                        created.FileId,
                        created.Name.Value,
                        created.Extension.Value,
                        created.Mime.Value,
                        created.Author,
                        created.Size.Value,
                        created.Hash.Value,
                        created.OccurredOnUtc.ToString("O", CultureInfo.InvariantCulture)), SerializerOptions),
                    createdUtc);
                return true;

            case FileRenamed renamed:
                entity = CreateOutboxEntity(
                    nameof(FileRenamed),
                    JsonSerializer.Serialize(new FileRenamedPayload(
                        renamed.FileId,
                        renamed.OldName.Value,
                        renamed.NewName.Value,
                        renamed.OccurredOnUtc.ToString("O", CultureInfo.InvariantCulture)), SerializerOptions),
                    createdUtc);
                return true;

            case FileMetadataUpdated metadataUpdated:
                var metadata = metadataUpdated.SystemMetadata;
                entity = CreateOutboxEntity(
                    nameof(FileMetadataUpdated),
                    JsonSerializer.Serialize(new FileMetadataUpdatedPayload(
                        metadataUpdated.FileId,
                        metadataUpdated.Mime.Value,
                        metadataUpdated.Author,
                        metadataUpdated.Title,
                        new FileSystemMetadataPayload(
                            (int)metadata.Attributes,
                            metadata.CreatedUtc.Value.ToString("O", CultureInfo.InvariantCulture),
                            metadata.LastWriteUtc.Value.ToString("O", CultureInfo.InvariantCulture),
                            metadata.LastAccessUtc.Value.ToString("O", CultureInfo.InvariantCulture),
                            metadata.OwnerSid,
                            metadata.HardLinkCount,
                            metadata.AlternateDataStreamCount),
                        metadataUpdated.OccurredOnUtc.ToString("O", CultureInfo.InvariantCulture)), SerializerOptions),
                    createdUtc);
                return true;

            case FileReadOnlyChanged readOnlyChanged:
                entity = CreateOutboxEntity(
                    nameof(FileReadOnlyChanged),
                    JsonSerializer.Serialize(new FileReadOnlyChangedPayload(
                        readOnlyChanged.FileId,
                        readOnlyChanged.IsReadOnly,
                        readOnlyChanged.OccurredOnUtc.ToString("O", CultureInfo.InvariantCulture)), SerializerOptions),
                    createdUtc);
                return true;

            case FileContentReplaced contentReplaced:
                entity = CreateOutboxEntity(
                    nameof(FileContentReplaced),
                    JsonSerializer.Serialize(new FileContentReplacedPayload(
                        contentReplaced.FileId,
                        contentReplaced.Hash.Value,
                        contentReplaced.Size.Value,
                        contentReplaced.Version,
                        contentReplaced.OccurredOnUtc.ToString("O", CultureInfo.InvariantCulture)), SerializerOptions),
                    createdUtc);
                return true;

            case FileValidityChanged validityChanged:
                entity = CreateOutboxEntity(
                    nameof(FileValidityChanged),
                    JsonSerializer.Serialize(new FileValidityChangedPayload(
                        validityChanged.FileId,
                        validityChanged.IssuedAt?.Value.ToString("O", CultureInfo.InvariantCulture),
                        validityChanged.ValidUntil?.Value.ToString("O", CultureInfo.InvariantCulture),
                        validityChanged.HasPhysicalCopy,
                        validityChanged.HasElectronicCopy,
                        validityChanged.OccurredOnUtc.ToString("O", CultureInfo.InvariantCulture)), SerializerOptions),
                    createdUtc);
                return true;

            case SearchReindexRequested:
                entity = null;
                return false;

            default:
                entity = null;
                return false;
        }
    }

    public static bool TryDeserialize(OutboxEventEntity entity, out IDomainEvent? domainEvent, out string? error)
    {
        try
        {
            switch (entity.Type)
            {
                case nameof(FileCreated):
                    {
                        var payload = JsonSerializer.Deserialize<FileCreatedPayload>(entity.PayloadJson, SerializerOptions);
                        if (payload is null)
                        {
                            break;
                        }

                        domainEvent = new FileCreated(
                            payload.FileId,
                            FileName.From(payload.Name),
                            FileExtension.From(payload.Extension),
                            MimeType.From(payload.Mime),
                            payload.Author,
                            ByteSize.From(payload.Size),
                            FileHash.From(payload.Hash),
                            UtcTimestamp.From(ParseTimestamp(payload.OccurredUtc)));
                        error = null;
                        return true;
                    }

                case nameof(FileRenamed):
                    {
                        var payload = JsonSerializer.Deserialize<FileRenamedPayload>(entity.PayloadJson, SerializerOptions);
                        if (payload is null)
                        {
                            break;
                        }

                        domainEvent = new FileRenamed(
                            payload.FileId,
                            FileName.From(payload.OldName),
                            FileName.From(payload.NewName),
                            UtcTimestamp.From(ParseTimestamp(payload.OccurredUtc)));
                        error = null;
                        return true;
                    }

                case nameof(FileMetadataUpdated):
                    {
                        var payload = JsonSerializer.Deserialize<FileMetadataUpdatedPayload>(entity.PayloadJson, SerializerOptions);
                        if (payload is null)
                        {
                            break;
                        }

                        var metadataPayload = payload.SystemMetadata;
                        var metadata = new FileSystemMetadata(
                            (FileAttributesFlags)metadataPayload.Attributes,
                            UtcTimestamp.From(ParseTimestamp(metadataPayload.CreatedUtc)),
                            UtcTimestamp.From(ParseTimestamp(metadataPayload.LastWriteUtc)),
                            UtcTimestamp.From(ParseTimestamp(metadataPayload.LastAccessUtc)),
                            metadataPayload.OwnerSid,
                            metadataPayload.HardLinkCount,
                            metadataPayload.AlternateDataStreamCount);

                        domainEvent = new FileMetadataUpdated(
                            payload.FileId,
                            MimeType.From(payload.Mime),
                            payload.Author,
                            payload.Title,
                            metadata,
                            UtcTimestamp.From(ParseTimestamp(payload.OccurredUtc)));
                        error = null;
                        return true;
                    }

                case nameof(FileReadOnlyChanged):
                    {
                        var payload = JsonSerializer.Deserialize<FileReadOnlyChangedPayload>(entity.PayloadJson, SerializerOptions);
                        if (payload is null)
                        {
                            break;
                        }

                        domainEvent = new FileReadOnlyChanged(
                            payload.FileId,
                            payload.IsReadOnly,
                            UtcTimestamp.From(ParseTimestamp(payload.OccurredUtc)));
                        error = null;
                        return true;
                    }

                case nameof(FileContentReplaced):
                    {
                        var payload = JsonSerializer.Deserialize<FileContentReplacedPayload>(entity.PayloadJson, SerializerOptions);
                        if (payload is null)
                        {
                            break;
                        }

                        domainEvent = new FileContentReplaced(
                            payload.FileId,
                            FileHash.From(payload.Hash),
                            ByteSize.From(payload.Size),
                            payload.Version,
                            UtcTimestamp.From(ParseTimestamp(payload.OccurredUtc)));
                        error = null;
                        return true;
                    }

                case nameof(FileValidityChanged):
                    {
                        var payload = JsonSerializer.Deserialize<FileValidityChangedPayload>(entity.PayloadJson, SerializerOptions);
                        if (payload is null)
                        {
                            break;
                        }

                        domainEvent = new FileValidityChanged(
                            payload.FileId,
                            payload.IssuedAt is null ? null : UtcTimestamp.From(ParseTimestamp(payload.IssuedAt)),
                            payload.ValidUntil is null ? null : UtcTimestamp.From(ParseTimestamp(payload.ValidUntil)),
                            payload.HasPhysicalCopy,
                            payload.HasElectronicCopy,
                            UtcTimestamp.From(ParseTimestamp(payload.OccurredUtc)));
                        error = null;
                        return true;
                    }
            }
        }
        catch (Exception ex)
        {
            domainEvent = null;
            error = ex.Message;
            return false;
        }

        domainEvent = null;
        error = "Unsupported or malformed outbox payload.";
        return false;
    }

    private static OutboxEventEntity CreateOutboxEntity(string type, string payload, DateTimeOffset createdUtc)
    {
        return new OutboxEventEntity
        {
            Id = Guid.NewGuid(),
            Type = type,
            PayloadJson = payload,
            CreatedUtc = createdUtc,
            Attempts = 0,
        };
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private sealed record FileCreatedPayload(Guid FileId, string Name, string Extension, string Mime, string Author, long Size, string Hash, string OccurredUtc);

    private sealed record FileRenamedPayload(Guid FileId, string OldName, string NewName, string OccurredUtc);

    private sealed record FileMetadataUpdatedPayload(Guid FileId, string Mime, string Author, string? Title, FileSystemMetadataPayload SystemMetadata, string OccurredUtc);

    private sealed record FileReadOnlyChangedPayload(Guid FileId, bool IsReadOnly, string OccurredUtc);

    private sealed record FileContentReplacedPayload(Guid FileId, string Hash, long Size, int Version, string OccurredUtc);

    private sealed record FileValidityChangedPayload(Guid FileId, string? IssuedAt, string? ValidUntil, bool HasPhysicalCopy, bool HasElectronicCopy, string OccurredUtc);

    private sealed record FileSystemMetadataPayload(int Attributes, string CreatedUtc, string LastWriteUtc, string LastAccessUtc, string? OwnerSid, uint? HardLinkCount, uint? AlternateDataStreamCount);
}
