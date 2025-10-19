using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.Domain.FileSystem.Events;
using Veriado.Domain.Files.Events;
using Veriado.Domain.Metadata;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Options;
using Xunit;

namespace Veriado.Application.Tests.Infrastructure;

public sealed class AuditEventProjectorTests
{
    [Fact]
    [Trait("Category", "SQLiteOnly")]
    public async Task Project_WritesExpectedAuditRows()
    {
        var databasePath = CreateDatabasePath();
        var provider = await BuildProviderAsync(databasePath);
        try
        {
            var projectorType = typeof(AppDbContext).Assembly.GetType("Veriado.Infrastructure.Events.AuditEventProjector")
                ?? throw new InvalidOperationException("AuditEventProjector type not found.");
            await using var scope = provider.CreateAsyncScope();
            var services = scope.ServiceProvider;

            var projector = ActivatorUtilities.CreateInstance(services, projectorType);
            await using var context = services.GetRequiredService<AppDbContext>();

            var fileId = Guid.NewGuid();
            var fileSystemId = Guid.NewGuid();
            var timestamp = UtcTimestamp.From(DateTimeOffset.UtcNow);
            var hash = FileHash.Compute(new byte[] { 1, 2, 3, 4 });
            var path = StoragePath.From("/tmp/sample");

            var events = new List<(Guid, IDomainEvent)>
            {
                (fileId, new FileCreated(fileId, FileName.From("contract"), FileExtension.From("pdf"), MimeType.From("application/pdf"), "author", ByteSize.From(1024), hash, timestamp)),
                (fileId, new FileValidityChanged(fileId, timestamp, timestamp, true, false, timestamp)),
                (fileId, new FileContentLinked(
                    fileId,
                    fileSystemId,
                    FileContentLink.Create(
                        "local",
                        path.Value,
                        hash,
                        ByteSize.From(1024),
                        ContentVersion.Initial,
                        timestamp,
                        MimeType.From("application/pdf")),
                    timestamp)),
                (fileSystemId, new FileSystemContentChanged(fileSystemId, StorageProvider.Local, path, hash, ByteSize.From(1024), MimeType.From("application/pdf"), ContentVersion.Initial, false, timestamp)),
                (fileSystemId, new FileSystemMissingDetected(fileSystemId, path, timestamp, null)),
            };

            var projectMethod = projectorType.GetMethod("Project")
                ?? throw new InvalidOperationException("AuditEventProjector.Project method not found.");

            var projected = (bool)projectMethod.Invoke(projector, new object[] { context, events })!;
            Assert.True(projected);

            await context.SaveChangesAsync().ConfigureAwait(false);

            var fileAudits = await context.FileAudits.ToListAsync().ConfigureAwait(false);
            Assert.Equal(2, fileAudits.Count);

            var linkAudits = await context.FileLinkAudits.ToListAsync().ConfigureAwait(false);
            var linkAudit = Assert.Single(linkAudits);
            Assert.Equal(fileSystemId, linkAudit.FileSystemId);

            var fileSystemAudits = await context.FileSystemAudits.ToListAsync().ConfigureAwait(false);
            Assert.Equal(2, fileSystemAudits.Count);
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
        return Path.Combine(directory, $"audit-projector-{Guid.NewGuid():N}.db");
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
