using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.Application.Tests.Domain.FileSystem;
using Veriado.Domain.FileSystem;
using Veriado.Domain.Files;
using Veriado.Domain.Metadata;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.DependencyInjection;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Options;
using Xunit;

namespace Veriado.Application.Tests.Infrastructure;

public sealed class FileSchemaTests
{
    [Fact]
    [Trait("Category", "SQLiteOnly")]
    public async Task InsertAndRetrieveFileSystemEntity_Succeeds()
    {
        var databasePath = CreateDatabasePath();
        var provider = await BuildProviderAsync(databasePath).ConfigureAwait(false);

        try
        {
            var entity = FileSystemEntityFactory.CreateSample();

            await using (var scope = provider.CreateAsyncScope())
            {
                await using var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.FileSystems.Add(entity);
                await context.SaveChangesAsync().ConfigureAwait(false);
            }

            await using var verificationScope = provider.CreateAsyncScope();
            await using var verificationContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var stored = await verificationContext.FileSystems.SingleAsync(f => f.Id == entity.Id).ConfigureAwait(false);

            Assert.Equal(entity.Path, stored.Path);
            Assert.Equal(entity.Hash, stored.Hash);
            Assert.Equal(entity.ContentVersion, stored.ContentVersion);
        }
        finally
        {
            await CleanupDatabaseAsync(provider, databasePath).ConfigureAwait(false);
        }
    }

    [Fact]
    [Trait("Category", "SQLiteOnly")]
    public async Task InsertAndRetrieveFileEntity_Succeeds()
    {
        var databasePath = CreateDatabasePath();
        var provider = await BuildProviderAsync(databasePath).ConfigureAwait(false);

        try
        {
            var fileSystem = FileSystemEntityFactory.CreateSample();
            var createdUtc = fileSystem.CreatedUtc;
            var metadata = new FileSystemMetadata(
                fileSystem.Attributes,
                fileSystem.CreatedUtc,
                fileSystem.LastWriteUtc,
                fileSystem.LastAccessUtc,
                fileSystem.OwnerSid,
                hardLinkCount: null,
                alternateDataStreamCount: null);

            var file = FileEntity.CreateNew(
                FileName.From("contract"),
                FileExtension.From("pdf"),
                MimeType.From("application/pdf"),
                "author",
                fileSystem.Id,
                fileSystem.Provider.ToString(),
                fileSystem.Path.Value,
                FileHash.From(new string('B', 64)),
                ByteSize.From(2048),
                ContentVersion.Initial,
                createdUtc,
                systemMetadata: metadata);

            await using (var scope = provider.CreateAsyncScope())
            {
                await using var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.FileSystems.Add(fileSystem);
                context.Files.Add(file);
                await context.SaveChangesAsync().ConfigureAwait(false);
            }

            await using var verificationScope = provider.CreateAsyncScope();
            await using var verificationContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();

            var stored = await verificationContext.Files.SingleAsync(f => f.Id == file.Id).ConfigureAwait(false);

            Assert.Equal(fileSystem.Id, stored.FileSystemId);
            Assert.Equal(file.ContentHash, stored.ContentHash);
            Assert.Equal(file.Size, stored.Size);
        }
        finally
        {
            await CleanupDatabaseAsync(provider, databasePath).ConfigureAwait(false);
        }
    }

    [Fact]
    [Trait("Category", "SQLiteOnly")]
    public async Task ForeignKey_RequiresExistingFileSystemEntity()
    {
        var databasePath = CreateDatabasePath();
        var provider = await BuildProviderAsync(databasePath).ConfigureAwait(false);

        try
        {
            var createdUtc = UtcTimestamp.From(DateTimeOffset.UtcNow);
            var metadata = new FileSystemMetadata(
                FileAttributesFlags.Normal,
                createdUtc,
                createdUtc,
                createdUtc,
                ownerSid: null,
                hardLinkCount: null,
                alternateDataStreamCount: null);

            var fileSystemId = Guid.NewGuid();
            var file = FileEntity.CreateNew(
                FileName.From("missing"),
                FileExtension.From("txt"),
                MimeType.From("text/plain"),
                "author",
                fileSystemId,
                StorageProvider.Local.ToString(),
                fileSystemId.ToString("D"),
                FileHash.From(new string('C', 64)),
                ByteSize.From(512),
                ContentVersion.Initial,
                createdUtc,
                systemMetadata: metadata);

            await using var scope = provider.CreateAsyncScope();
            await using var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            context.Files.Add(file);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync()).ConfigureAwait(false);
            var sqlite = Assert.IsType<SqliteException>(exception.InnerException);
            Assert.Equal(19, sqlite.SqliteErrorCode); // SQLITE_CONSTRAINT
            Assert.Contains("FOREIGN KEY", sqlite.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await CleanupDatabaseAsync(provider, databasePath).ConfigureAwait(false);
        }
    }

    [Fact]
    [Trait("Category", "SQLiteOnly")]
    public async Task UniqueConstraint_PreventsDuplicateFileSystemLinks()
    {
        var databasePath = CreateDatabasePath();
        var provider = await BuildProviderAsync(databasePath).ConfigureAwait(false);

        try
        {
            var fileSystem = FileSystemEntityFactory.CreateSample();
            var createdUtc = fileSystem.CreatedUtc;
            var metadata = new FileSystemMetadata(
                fileSystem.Attributes,
                fileSystem.CreatedUtc,
                fileSystem.LastWriteUtc,
                fileSystem.LastAccessUtc,
                fileSystem.OwnerSid,
                hardLinkCount: null,
                alternateDataStreamCount: null);

            var firstFile = FileEntity.CreateNew(
                FileName.From("primary"),
                FileExtension.From("txt"),
                MimeType.From("text/plain"),
                "author",
                fileSystem.Id,
                fileSystem.Provider.ToString(),
                fileSystem.Path.Value,
                FileHash.From(new string('D', 64)),
                ByteSize.From(1024),
                ContentVersion.Initial,
                createdUtc,
                systemMetadata: metadata);

            var duplicateFile = FileEntity.CreateNew(
                FileName.From("secondary"),
                FileExtension.From("txt"),
                MimeType.From("text/plain"),
                "author",
                fileSystem.Id,
                fileSystem.Provider.ToString(),
                fileSystem.Path.Value,
                FileHash.From(new string('E', 64)),
                ByteSize.From(2048),
                ContentVersion.Initial,
                createdUtc,
                systemMetadata: metadata);

            await using var scope = provider.CreateAsyncScope();
            await using var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            context.FileSystems.Add(fileSystem);
            context.Files.Add(firstFile);
            context.Files.Add(duplicateFile);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync()).ConfigureAwait(false);
            var sqlite = Assert.IsType<SqliteException>(exception.InnerException);
            Assert.Equal(19, sqlite.SqliteErrorCode); // SQLITE_CONSTRAINT
            Assert.Contains("files.filesystem_id", sqlite.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await CleanupDatabaseAsync(provider, databasePath).ConfigureAwait(false);
        }
    }

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "veriado-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"file-schema-{Guid.NewGuid():N}.db");
    }

    private static async Task<ServiceProvider> BuildProviderAsync(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(options =>
        {
            options.DbPath = databasePath;
            options.BatchMaxItems = 4;
            options.BatchMaxWindowMs = 10;
        });

        var provider = services.BuildServiceProvider();
        await provider.InitializeInfrastructureAsync().ConfigureAwait(false);
        return provider;
    }

    private static async Task CleanupDatabaseAsync(ServiceProvider provider, string path)
    {
        if (provider is not null)
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
