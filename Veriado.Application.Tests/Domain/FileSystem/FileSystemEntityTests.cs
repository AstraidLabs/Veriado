using System;
using Veriado.Domain.FileSystem;
using Veriado.Domain.FileSystem.Events;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;
using Xunit;

namespace Veriado.Application.Tests.Domain.FileSystem;

public static class FileSystemEntityFactory
{
    public static FileSystemEntity CreateSample()
    {
        return FileSystemEntity.CreateNew(
            StorageProvider.Local,
            StoragePath.From("/content/sample.bin"),
            FileHash.From(new string('A', 64)),
            ByteSize.From(1024),
            MimeType.From("application/octet-stream"),
            FileAttributesFlags.None,
            ownerSid: null,
            isEncrypted: false,
            createdUtc: UtcTimestamp.From(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            lastWriteUtc: UtcTimestamp.From(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            lastAccessUtc: UtcTimestamp.From(new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero)));
    }
}

public class FileSystemEntityTests
{
    [Fact]
    public void ReplaceContent_IncrementsVersionOnlyWhenHashChanges()
    {
        var entity = FileSystemEntityFactory.CreateSample();
        var originalVersion = entity.ContentVersion;
        var expectedNextVersion = originalVersion.Next();

        entity.ClearDomainEvents();

        entity.ReplaceContent(
            StoragePath.From("/content/sample.bin"),
            FileHash.From(new string('A', 64)),
            ByteSize.From(1024),
            MimeType.From("application/octet-stream"),
            isEncrypted: false,
            whenUtc: UtcTimestamp.From(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(originalVersion, entity.ContentVersion);
        Assert.Empty(entity.DomainEvents);

        entity.ReplaceContent(
            StoragePath.From("/content/sample.bin"),
            FileHash.From(new string('B', 64)),
            ByteSize.From(1024),
            MimeType.From("application/octet-stream"),
            isEncrypted: false,
            whenUtc: UtcTimestamp.From(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(expectedNextVersion, entity.ContentVersion);
        var @event = Assert.IsType<FileSystemContentChanged>(Assert.Single(entity.DomainEvents));
        Assert.Equal(expectedNextVersion, @event.ContentVersion);
    }

    [Fact]
    public void MarkMissing_IsIdempotent()
    {
        var entity = FileSystemEntityFactory.CreateSample();
        entity.ClearDomainEvents();

        var firstDetection = UtcTimestamp.From(new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero));
        entity.MarkMissing(firstDetection);

        var evt = Assert.IsType<FileSystemMissingDetected>(Assert.Single(entity.DomainEvents));
        Assert.Equal(firstDetection, entity.MissingSinceUtc);
        Assert.Equal(firstDetection, evt.MissingSinceUtc);
        Assert.True(entity.IsMissing);

        var laterDetection = UtcTimestamp.From(new DateTimeOffset(2024, 4, 2, 0, 0, 0, TimeSpan.Zero));
        entity.MarkMissing(laterDetection);

        Assert.Single(entity.DomainEvents);
        Assert.Equal(firstDetection, entity.MissingSinceUtc);
        Assert.True(entity.IsMissing);
    }
}
