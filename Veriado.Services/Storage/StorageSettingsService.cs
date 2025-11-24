using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.FileSystem;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Entities;
using Veriado.Infrastructure.Storage;

namespace Veriado.Services.Storage;

public sealed class StorageSettingsService : IStorageSettingsService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IFilePathResolver _pathResolver;
    private readonly ILogger<StorageSettingsService> _logger;

    public StorageSettingsService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IFilePathResolver pathResolver,
        ILogger<StorageSettingsService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StorageSettingsDto> GetStorageSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var root = await dbContext.StorageRoots
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasFiles = await dbContext.FileSystems
            .AsNoTracking()
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        return new StorageSettingsDto
        {
            CurrentRootPath = root?.RootPath,
            CanChangeRoot = !hasFiles,
        };
    }

    public async Task<ChangeStorageRootResult> ChangeStorageRootAsync(string newRootPath, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var hasFiles = await dbContext.FileSystems
            .AsNoTracking()
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        if (hasFiles)
        {
            return ChangeStorageRootResult.CatalogNotEmpty;
        }

        if (string.IsNullOrWhiteSpace(newRootPath))
        {
            return ChangeStorageRootResult.InvalidPath;
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = StorageRootValidator.ValidateWritableRoot(newRootPath, _logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Invalid storage root path {RootPath}.", newRootPath);
            return ChangeStorageRootResult.InvalidPath;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            _logger.LogError(ex, "I/O error while validating storage root {RootPath}.", newRootPath);
            return ChangeStorageRootResult.IoError;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while validating storage root {RootPath}.", newRootPath);
            return ChangeStorageRootResult.UnknownError;
        }

        try
        {
            Directory.CreateDirectory(normalizedRoot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            _logger.LogError(ex, "I/O error while ensuring storage root exists {RootPath}.", normalizedRoot);
            return ChangeStorageRootResult.IoError;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while creating storage root directory {RootPath}.", normalizedRoot);
            return ChangeStorageRootResult.UnknownError;
        }

        try
        {
            var existing = await dbContext.StorageRoots
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                await dbContext.StorageRoots
                    .AddAsync(new FileStorageRootEntity(normalizedRoot), cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                existing.UpdateRootPath(normalizedRoot);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (_pathResolver is FilePathResolver resolver)
            {
                resolver.OverrideCachedRoot(normalizedRoot);
            }
            else
            {
                _pathResolver.InvalidateRootCache();
            }

            return ChangeStorageRootResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            _logger.LogError(ex, "I/O error while persisting storage root {RootPath}.", normalizedRoot);
            return ChangeStorageRootResult.IoError;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while updating storage root {RootPath}.", normalizedRoot);
            return ChangeStorageRootResult.UnknownError;
        }
    }
}
