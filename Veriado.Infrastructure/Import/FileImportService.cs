using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Files;
using Veriado.Domain.Search;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Import;

/// <summary>
/// Provides a high-throughput import pipeline for file aggregates.
/// </summary>
public sealed class FileImportService
{
    private const int MinimumChunkSize = 500;
    private const int MaximumChunkSize = 2000;

    private static readonly ConstructorInfo FileEntityConstructor = typeof(FileEntity)
        .GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[]
            {
                typeof(Guid),
                typeof(FileName),
                typeof(FileExtension),
                typeof(MimeType),
                typeof(string),
                typeof(Guid),
                typeof(FileHash),
                typeof(ByteSize),
                typeof(ContentVersion),
                typeof(UtcTimestamp),
                typeof(FileSystemMetadata),
                typeof(string),
            },
            modifiers: null)
        ?? throw new InvalidOperationException("FileEntity private constructor could not be resolved.");

    private static readonly PropertyInfo LastModifiedProperty = typeof(FileEntity)
        .GetProperty(nameof(FileEntity.LastModifiedUtc), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException("LastModifiedUtc property is missing.");

    private static readonly PropertyInfo IsReadOnlyProperty = typeof(FileEntity)
        .GetProperty(nameof(FileEntity.IsReadOnly), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException("IsReadOnly property is missing.");

    private static readonly PropertyInfo ContentRevisionProperty = typeof(FileEntity)
        .GetProperty(nameof(FileEntity.ContentRevision), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException("ContentRevision property is missing.");

    private static readonly PropertyInfo SearchIndexProperty = typeof(FileEntity)
        .GetProperty(nameof(FileEntity.SearchIndex), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException("SearchIndex property is missing.");

    private static readonly PropertyInfo ValidityProperty = typeof(FileEntity)
        .GetProperty(nameof(FileEntity.Validity), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException("Validity property is missing.");

    private static readonly PropertyInfo FtsPolicyProperty = typeof(FileEntity)
        .GetProperty(nameof(FileEntity.FtsPolicy), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException("FtsPolicy property is missing.");

    private readonly AppDbContext _dbContext;
    private readonly IFileSearchProjection _searchProjection;
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;
    private readonly IClock _clock;

    public FileImportService(
        AppDbContext dbContext,
        IFileSearchProjection searchProjection,
        ISearchIndexSignatureCalculator signatureCalculator,
        IClock clock)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _searchProjection = searchProjection ?? throw new ArgumentNullException(nameof(searchProjection));
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task ImportAsync(IEnumerable<NewFileDto> batch, CancellationToken cancellationToken, int chunkSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedChunkSize = Math.Clamp(chunkSize, MinimumChunkSize, MaximumChunkSize);
        using var enumerator = batch.GetEnumerator();
        var buffer = new List<NewFileDto>(normalizedChunkSize);

        while (true)
        {
            buffer.Clear();

            while (buffer.Count < normalizedChunkSize && enumerator.MoveNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                buffer.Add(enumerator.Current);
            }

            if (buffer.Count == 0)
            {
                break;
            }

            var deduped = Deduplicate(buffer);
            await ImportChunkAsync(deduped, cancellationToken).ConfigureAwait(false);

            if (buffer.Count < normalizedChunkSize)
            {
                break;
            }
        }
    }

    private async Task ImportChunkAsync(IReadOnlyList<NewFileDto> chunk, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (chunk.Count == 0)
        {
            return;
        }

        var ids = new HashSet<Guid>(chunk.Count);
        foreach (var dto in chunk)
        {
            ids.Add(dto.FileId);
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingEntities = await _dbContext.Files
            .Where(file => ids.Contains(file.Id))
            .Include(file => file.Validity)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingMap = existingEntities.ToDictionary(file => file.Id);
        var dtoLookup = chunk.ToDictionary(dto => dto.FileId);
        var tracked = new List<FileEntity>(chunk.Count);

        foreach (var dto in chunk)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entity = CreateEntity(dto);
            tracked.Add(entity);

            if (existingMap.TryGetValue(dto.FileId, out var existing))
            {
                _dbContext.Entry(existing).State = EntityState.Detached;
                _dbContext.Files.Update(entity);
            }
            else
            {
                _dbContext.Files.Add(entity);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var entity in tracked)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _searchProjection.UpsertAsync(entity, cancellationToken).ConfigureAwait(false);
            var dto = dtoLookup[entity.Id];
            var signature = _signatureCalculator.Compute(entity);
            var indexedAt = dto.SearchIndexedUtc ?? UtcTimestamp.From(_clock.UtcNow);
            var indexedTitle = string.IsNullOrWhiteSpace(dto.SearchIndexedTitle)
                ? signature.NormalizedTitle
                : dto.SearchIndexedTitle!;

            entity.ConfirmIndexed(
                dto.SearchSchemaVersion <= 0 ? 1 : dto.SearchSchemaVersion,
                indexedAt,
                signature.AnalyzerVersion,
                signature.TokenHash,
                indexedTitle);
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static FileEntity CreateEntity(NewFileDto dto)
    {
        if (dto.SearchSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dto.SearchSchemaVersion), dto.SearchSchemaVersion, "Schema version must be positive.");
        }

        var entity = (FileEntity)FileEntityConstructor.Invoke(
            new object?[]
            {
                dto.FileId,
                dto.Name,
                dto.Extension,
                dto.Mime,
                dto.Author,
                dto.FileSystemId,
                dto.ContentHash,
                dto.Size,
                dto.LinkedContentVersion,
                dto.CreatedUtc,
                dto.SystemMetadata,
                dto.Title,
            });

        LastModifiedProperty.SetValue(entity, dto.LastModifiedUtc);
        IsReadOnlyProperty.SetValue(entity, dto.IsReadOnly);
        ContentRevisionProperty.SetValue(entity, dto.Version);
        FtsPolicyProperty.SetValue(entity, dto.FtsPolicy ?? Fts5Policy.Default);

        var searchIndex = new SearchIndexState(dto.SearchSchemaVersion, isStale: true);
        SearchIndexProperty.SetValue(entity, searchIndex);

        if (dto.Validity is not null)
        {
            var validity = new FileDocumentValidityEntity(
                dto.Validity.IssuedAt,
                dto.Validity.ValidUntil,
                dto.Validity.HasPhysicalCopy,
                dto.Validity.HasElectronicCopy);
            ValidityProperty.SetValue(entity, validity);
        }
        else
        {
            ValidityProperty.SetValue(entity, null);
        }

        return entity;
    }

    private static IReadOnlyList<NewFileDto> Deduplicate(IReadOnlyList<NewFileDto> input)
    {
        if (input.Count == 0)
        {
            return Array.Empty<NewFileDto>();
        }

        var order = new List<NewFileDto>(input.Count);
        var index = new Dictionary<Guid, int>();

        for (var i = 0; i < input.Count; i++)
        {
            var dto = input[i];
            if (index.TryGetValue(dto.FileId, out var existingIndex))
            {
                order[existingIndex] = dto;
            }
            else
            {
                index[dto.FileId] = order.Count;
                order.Add(dto);
            }
        }

        return order;
    }
}
