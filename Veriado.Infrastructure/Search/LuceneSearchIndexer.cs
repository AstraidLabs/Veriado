using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using Veriado.Domain.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides Lucene.Net backed indexing for search documents.
/// </summary>
internal sealed class LuceneSearchIndexer : IDisposable, IAsyncDisposable
{
    private const string GuidFormat = "N";
    private const int CommitThreshold = 32;
    private static readonly TimeSpan CommitInterval = TimeSpan.FromSeconds(2);

    private readonly InfrastructureOptions _options;
    private readonly Analyzer? _analyzer;
    private readonly FSDirectory? _directory;
    private readonly IndexWriter? _writer;
    private readonly ILogger<LuceneSearchIndexer>? _logger;
    private readonly SemaphoreSlim _writerLock = new(1, 1);
    private Task? _scheduledCommit;
    private CancellationTokenSource? _scheduledCommitCts;
    private int _pendingOperations;
    private bool _disposed;

    public LuceneSearchIndexer(InfrastructureOptions options, ILogger<LuceneSearchIndexer>? logger = null)
    {
        _options = options;
        _logger = logger;
        if (!options.EnableLuceneIntegration)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.LuceneIndexPath))
        {
            return;
        }

        var directoryInfo = new DirectoryInfo(options.LuceneIndexPath);
        if (!directoryInfo.Exists)
        {
            directoryInfo.Create();
        }

        var directory = FSDirectory.Open(directoryInfo);
        if (!TryClearStaleWriteLock(directory))
        {
            directory.Dispose();
            return;
        }

        _analyzer = new LuceneMetadataAnalyzer();
        _directory = directory;
        _writer = CreateWriter();
        _writer.Commit();
    }

    public bool IsEnabled
        => _options.EnableLuceneIntegration && _directory is not null && _analyzer is not null && _writer is not null;

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
            Writer.UpdateDocument(new Term(LuceneSearchFields.Id, document.FileId.ToString(GuidFormat, CultureInfo.InvariantCulture)), luceneDocument);
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
            Writer.DeleteDocuments(new Term(LuceneSearchFields.Id, fileId.ToString(GuidFormat, CultureInfo.InvariantCulture)));
            CommitOrSchedule();
        }
        finally
        {
            _writerLock.Release();
        }
    }

    private IndexWriter CreateWriter()
    {
        if (_directory is null || _analyzer is null)
        {
            throw new InvalidOperationException("Lucene index directory has not been initialised.");
        }

        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer)
        {
            OpenMode = OpenMode.CREATE_OR_APPEND,
        };

        return new IndexWriter(_directory, config);
    }

    private bool TryClearStaleWriteLock(FSDirectory directory)
    {
        try
        {
            if (!IndexWriter.IsLocked(directory))
            {
                return true;
            }

            IndexWriter.Unlock(directory);
            _logger?.LogWarning(
                "Detected and cleared a stale Lucene write lock at {LuceneIndexPath}.",
                _options.LuceneIndexPath);
            return true;
        }
        catch (IOException ex)
        {
            _logger?.LogError(
                ex,
                "Detected a stale Lucene write lock at {LuceneIndexPath} but failed to clear it. Lucene integration will be disabled.",
                _options.LuceneIndexPath);
            return false;
        }
    }

    private static Document BuildDocument(SearchDocument document)
    {
        var luceneDocument = new Document
        {
            new StringField(LuceneSearchFields.Id, document.FileId.ToString(GuidFormat, CultureInfo.InvariantCulture), Field.Store.YES),
            new TextField(LuceneSearchFields.Title, document.Title ?? string.Empty, Field.Store.YES),
            new TextField(LuceneSearchFields.Author, document.Author ?? string.Empty, Field.Store.YES),
            new TextField(LuceneSearchFields.Metadata, document.MetadataJson ?? string.Empty, Field.Store.YES),
            new StringField(LuceneSearchFields.Mime, document.Mime ?? string.Empty, Field.Store.YES),
            new StringField(LuceneSearchFields.Created, document.CreatedUtc.ToString("O", CultureInfo.InvariantCulture), Field.Store.YES),
            new StringField(LuceneSearchFields.Modified, document.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture), Field.Store.YES),
        };

        return luceneDocument;
    }

    private void CommitOrSchedule()
    {
        if (_writer is null)
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
        Writer.Commit();
        _pendingOperations = 0;
    }

    private void ScheduleCommit()
    {
        CancelScheduledCommit(waitForCompletion: false);

        if (_writer is null)
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
        if (_pendingOperations <= 0 || _writer is null)
        {
            return;
        }

        Writer.Commit();
        _pendingOperations = 0;
    }

    private IndexWriter Writer => _writer ?? throw new InvalidOperationException("Lucene index writer has not been initialised.");

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

        if (_writer is not null)
        {
            _writerLock.Wait();
            try
            {
                FlushPendingOperations();
                _writer.Dispose();
            }
            finally
            {
                _writerLock.Release();
            }
        }

        _analyzer?.Dispose();
        _directory?.Dispose();
        _writerLock.Dispose();
    }
}
