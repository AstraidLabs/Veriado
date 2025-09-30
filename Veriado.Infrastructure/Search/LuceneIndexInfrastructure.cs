using System;
using System.IO;
using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Hosts the long-lived Lucene index writer and related resources shared across the application lifetime.
/// </summary>
internal sealed class LuceneIndexInfrastructure : IDisposable
{

    private readonly InfrastructureOptions _options;
    private readonly ILogger<LuceneIndexInfrastructure>? _logger;
    private Analyzer? _analyzer;
    private FSDirectory? _directory;
    private IndexWriter? _writer;
    private bool _disposed;

    public LuceneIndexInfrastructure(InfrastructureOptions options, ILogger<LuceneIndexInfrastructure>? logger = null)
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

    public Analyzer? Analyzer => _analyzer;

    public FSDirectory? Directory => _directory;

    public IndexWriter? Writer => _writer;

    public bool IsEnabled => _options.EnableLuceneIntegration && _analyzer is not null && _directory is not null && _writer is not null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _writer?.Dispose();
        _writer = null;
        _analyzer?.Dispose();
        _analyzer = null;
        _directory?.Dispose();
        _directory = null;
    }

    private IndexWriter CreateWriter()
    {
        if (_directory is null || _analyzer is null)
        {
            throw new InvalidOperationException("Lucene index infrastructure has not been initialised.");
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
}
