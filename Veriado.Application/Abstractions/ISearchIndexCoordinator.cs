using System;
using Microsoft.EntityFrameworkCore;
using Veriado.Domain.Search.Events;

namespace Veriado.Appl.Abstractions;

public interface ISearchIndexCoordinator
{
    Task EnqueueAsync(DbContext dbContext, Guid fileId, ReindexReason reason, DateTimeOffset requestedUtc, CancellationToken cancellationToken);

    Task<SearchIndexUpdateResult> ReindexAsync(Guid fileId, ReindexReason reason, CancellationToken cancellationToken);
}

public enum SearchIndexUpdateStatus
{
    Succeeded,
    NoChanges,
    NotFound,
    Failed,
}

public readonly record struct SearchIndexUpdateResult(SearchIndexUpdateStatus Status, Exception? Exception, bool Updated)
{
    public static SearchIndexUpdateResult Success(bool updated) => new(SearchIndexUpdateStatus.Succeeded, null, updated);

    public static SearchIndexUpdateResult NoChanges() => new(SearchIndexUpdateStatus.NoChanges, null, false);

    public static SearchIndexUpdateResult NotFound() => new(SearchIndexUpdateStatus.NotFound, null, false);

    public static SearchIndexUpdateResult Failed(Exception exception) => new(SearchIndexUpdateStatus.Failed, exception, false);
}
