using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Entities;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.FileSystem;

/// <summary>
/// Ensures that a storage root exists and is persisted in the database.
/// </summary>
internal static class StorageRootInitializer
{
    /// <summary>
    /// Ensures that the storage root is present in the database and on disk.
    /// </summary>
    public static async Task EnsureStorageRootAsync(
        AppDbContext dbContext,
        InfrastructureOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var existingRoot = await dbContext.StorageRoots
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existingRoot is not null)
        {
            var normalized = Path.GetFullPath(existingRoot.RootPath);
            Directory.CreateDirectory(normalized);
            logger.LogInformation("Using configured storage root {RootPath}.", normalized);
            return;
        }

        string rootPath;
        if (!string.IsNullOrWhiteSpace(options.StorageRootOverride))
        {
            rootPath = Path.GetFullPath(options.StorageRootOverride!);
        }
        else
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            rootPath = Path.Combine(documents, "Veriado");
            rootPath = Path.GetFullPath(rootPath);
        }

        Directory.CreateDirectory(rootPath);

        dbContext.StorageRoots.Add(new FileStorageRootEntity(rootPath));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Initialized storage root at {RootPath}.", rootPath);
    }
}
