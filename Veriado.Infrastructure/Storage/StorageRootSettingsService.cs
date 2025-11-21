using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Application.Abstractions;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Entities;

namespace Veriado.Infrastructure.Storage;

public sealed class StorageRootSettingsService : IStorageRootSettingsService
{
    private const string DefaultFolderName = "Veriado";

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IFilePathResolver _filePathResolver;
    private readonly ILogger<StorageRootSettingsService> _logger;

    public StorageRootSettingsService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IFilePathResolver filePathResolver,
        ILogger<StorageRootSettingsService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _filePathResolver = filePathResolver ?? throw new ArgumentNullException(nameof(filePathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> GetCurrentRootAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var root = await dbContext.StorageRoots
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return root?.RootPath ?? string.Empty;
    }

    public async Task<string> GetEffectiveRootAsync(CancellationToken cancellationToken)
    {
        var current = await GetCurrentRootAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(current))
        {
            return current;
        }

        var defaultRoot = GetDefaultRootPath();
        var normalizedDefault = StorageRootValidator.ValidateWritableRoot(defaultRoot, _logger);
        await PersistRootIfMissingAsync(normalizedDefault, cancellationToken).ConfigureAwait(false);
        return normalizedDefault;
    }

    public async Task ChangeRootAsync(string newRoot, CancellationToken cancellationToken)
    {
        var normalizedRoot = StorageRootValidator.ValidateWritableRoot(newRoot, _logger);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await dbContext.StorageRoots
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await dbContext.StorageRoots.AddAsync(new FileStorageRootEntity(normalizedRoot), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            existing.UpdateRootPath(normalizedRoot);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _filePathResolver.InvalidateRootCache();
    }

    private static string GetDefaultRootPath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(documents, DefaultFolderName);
    }

    private async Task PersistRootIfMissingAsync(string normalizedRoot, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await dbContext.StorageRoots
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await dbContext.StorageRoots.AddAsync(new FileStorageRootEntity(normalizedRoot), cancellationToken)
                .ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _filePathResolver.InvalidateRootCache();
        }
    }
}
