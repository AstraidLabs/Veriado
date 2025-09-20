using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Domain.Files;
using Veriado.Domain.Primitives;
using Veriado.Domain.Search;
using Veriado.Domain.ValueObjects;

namespace Veriado.Application.Abstractions;

/// <summary>
/// Provides access to persistence operations for the <see cref="FileEntity"/> aggregate.
/// </summary>
public interface IFileRepository
{
    Task<FileEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileEntity>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    IAsyncEnumerable<FileEntity> StreamAllAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsByHashAsync(FileHash hash, CancellationToken cancellationToken = default);

    Task AddAsync(FileEntity entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(FileEntity entity, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides full-text indexing functionality for search documents.
/// </summary>
public interface ISearchIndexer
{
    Task IndexAsync(SearchDocument document, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides query capabilities against the full-text search index.
/// </summary>
public interface ISearchQueryService
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int? limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides text extraction services for binary file content.
/// </summary>
public interface ITextExtractor
{
    Task<string?> ExtractTextAsync(FileEntity file, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes domain events after transactions commit.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
