using System.Linq;
using Veriado.Domain.Files;
using Veriado.Domain.Search.Events;
using Veriado.Domain.ValueObjects;
using Xunit;

namespace Veriado.Application.Tests.Domain.Files;

public static class FileEntityFactory
{
    public static FileEntity CreateSample(Guid? fileSystemId = null)
    {
        var resolvedFileSystemId = fileSystemId ?? Guid.NewGuid();
        var provider = StorageProvider.Local.ToString();
        var location = resolvedFileSystemId.ToString("D");
        return FileEntity.CreateNew(
            FileName.From("document.pdf"),
            FileExtension.From("pdf"),
            MimeType.From("application/pdf"),
            "Initial Author",
            resolvedFileSystemId,
            provider,
            location,
            FileHash.From(new string('A', 64)),
            ByteSize.From(1024),
            ContentVersion.Initial,
            UtcTimestamp.From(DateTimeOffset.UtcNow));
    }
}

public class FileEntityTests
{
    [Fact]
    public void UpdateMetadata_RaisesSearchReindexRequested()
    {
        var file = FileEntityFactory.CreateSample();
        file.ClearDomainEvents();

        var when = UtcTimestamp.From(DateTimeOffset.UtcNow.AddMinutes(5));

        file.UpdateMetadata(MimeType.From("application/msword"), "Updated Author", when);

        var reindexEvent = Assert.IsType<SearchReindexRequested>(Assert.Single(file.DomainEvents.Where(evt => evt is SearchReindexRequested)));
        Assert.Equal(file.Id, reindexEvent.FileId);
        Assert.Equal(ReindexReason.MetadataChanged, reindexEvent.Reason);
    }
}
