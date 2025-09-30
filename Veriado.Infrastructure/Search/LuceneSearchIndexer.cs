using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Microsoft.Extensions.Logging;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides Lucene.Net backed indexing for search documents.
/// </summary>
internal sealed class LuceneSearchIndexer : IDisposable, IAsyncDisposable
{
    private const int CommitThreshold = 32;
    private static readonly TimeSpan CommitInterval = TimeSpan.FromSeconds(2);

    private readonly LuceneIndexInfrastructure _infrastructure;
    private readonly ILogger<LuceneSearchIndexer>? _logger;
    private readonly SemaphoreSlim _writerLock = new(1, 1);
    private Task? _scheduledCommit;
    private CancellationTokenSource? _scheduledCommitCts;
    private int _pendingOperations;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance of the <see cref="LuceneSearchIndexer"/>.
    /// The indexer is registered as a singleton so that the underlying Lucene <see cref="IndexWriter"/>
    /// remains open for the application lifetime. This avoids per-operation open/close overhead and
    /// allows batched commit scheduling through <see cref="CommitOrSchedule"/>.
    /// </summary>
    public LuceneSearchIndexer(
        LuceneIndexInfrastructure infrastructure,
        ILogger<LuceneSearchIndexer>? logger = null)
    {
        _infrastructure = infrastructure ?? throw new ArgumentNullException(nameof(infrastructure));
        _logger = logger;
    }

    public bool IsEnabled => _infrastructure.IsEnabled;

    public async Task IndexAsync(SearchDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsEnabled)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var luceneDocument = BuildDocument(document);
            Writer.UpdateDocument(new Term(LuceneSearchFields.Id, document.FileId.ToString("N", CultureInfo.InvariantCulture)), luceneDocument);
            CommitOrSchedule();
        }
        finally
        {
            _writerLock.Release();
        }
    }

    public async Task DeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Writer.DeleteDocuments(new Term(LuceneSearchFields.Id, fileId.ToString("N", CultureInfo.InvariantCulture)));
            CommitOrSchedule();
        }
        finally
        {
            _writerLock.Release();
        }
    }

    private static Document BuildDocument(SearchDocument document)
    {
        var luceneDocument = new Document
        {
            new StringField(LuceneSearchFields.Id, document.FileId.ToString("N", CultureInfo.InvariantCulture), Field.Store.YES),
            new TextField(LuceneSearchFields.Title, document.Title ?? string.Empty, Field.Store.YES),
            new TextField(LuceneSearchFields.Author, document.Author ?? string.Empty, Field.Store.YES),
            new StringField(LuceneSearchFields.Mime, document.Mime ?? string.Empty, Field.Store.YES),
            new StringField(LuceneSearchFields.Created, document.CreatedUtc.ToString("O", CultureInfo.InvariantCulture), Field.Store.YES),
            new StringField(LuceneSearchFields.Modified, document.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture), Field.Store.YES),
        };

        if (!string.IsNullOrWhiteSpace(document.MetadataText))
        {
            luceneDocument.Add(new TextField(LuceneSearchFields.MetadataText, document.MetadataText!, Field.Store.YES));
        }

        if (!string.IsNullOrWhiteSpace(document.MetadataJson))
        {
            luceneDocument.Add(new TextField(LuceneSearchFields.Metadata, document.MetadataJson!, Field.Store.YES));
        }
        else
        {
            luceneDocument.Add(new StoredField(LuceneSearchFields.Metadata, string.Empty));
        }

        return luceneDocument;
    }

    private void CommitOrSchedule()
    {
        if (!IsEnabled)
        {
            return;
        }

        _pendingOperations++;

        if (_pendingOperations >= CommitThreshold)
        {
            CommitNow();
            return;
        }

        ScheduleCommit();
    }

    private void CommitNow()
    {
        CancelScheduledCommit(waitForCompletion: false);
        if (IsEnabled)
        {
            Writer.Commit();
            _pendingOperations = 0;
        }
    }

    private void ScheduleCommit()
    {
        CancelScheduledCommit(waitForCompletion: false);

        if (!IsEnabled)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _scheduledCommitCts = cts;
        _scheduledCommit = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(CommitInterval, cts.Token).ConfigureAwait(false);
                var acquired = false;
                try
                {
                    await _writerLock.WaitAsync(cts.Token).ConfigureAwait(false);
                    acquired = true;
                    if (_pendingOperations > 0)
                    {
                        Writer.Commit();
                        _pendingOperations = 0;
                    }
                }
                finally
                {
                    if (acquired)
                    {
                        _writerLock.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected when a manual commit occurs or disposal begins.
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lucene scheduled commit failed.");
            }
            finally
            {
                cts.Dispose();
                _scheduledCommitCts = null;
            }
        }, CancellationToken.None);
    }

    private void CancelScheduledCommit(bool waitForCompletion)
    {
        var scheduled = _scheduledCommit;
        var cts = _scheduledCommitCts;
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore, the commit task has already cleaned up.
            }
        }

        if (waitForCompletion && scheduled is not null)
        {
            try
            {
                scheduled.Wait();
            }
            catch (AggregateException ex) when (ex.InnerExceptions.TrueForAll(static e => e is OperationCanceledException or TaskCanceledException))
            {
                // Swallow cancellations triggered during shutdown.
            }
        }

        _scheduledCommitCts = null;
        _scheduledCommit = null;
    }

    private void FlushPendingOperations()
    {
        if (_pendingOperations <= 0 || !IsEnabled)
        {
            return;
        }

        Writer.Commit();
        _pendingOperations = 0;
    }

    private IndexWriter Writer => _infrastructure.Writer ?? throw new InvalidOperationException("Lucene index writer has not been initialised.");

    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private void DisposeCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CancelScheduledCommit(waitForCompletion: true);

        if (IsEnabled)
        {
            _writerLock.Wait();
            try
            {
                FlushPendingOperations();
            }
            finally
            {
                _writerLock.Release();
            }
        }

        _writerLock.Dispose();
    }
}
