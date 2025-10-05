using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using Veriado.Domain.Search;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides shared Lucene.NET primitives for indexing and querying search documents.
/// </summary>
internal sealed class LuceneIndexManager : IDisposable
{
    private readonly InfrastructureOptions _options;
    private readonly ILogger<LuceneIndexManager> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private FSDirectory? _directory;
    private Analyzer? _analyzer;
    private IndexWriter? _writer;
    private bool _disposed;

    public LuceneIndexManager(InfrastructureOptions options, ILogger<LuceneIndexManager> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Analyzer Analyzer
    {
        get
        {
            if (_analyzer is null)
            {
                throw new InvalidOperationException("Lucene index has not been initialised.");
            }

            return _analyzer;
        }
    }

    public FSDirectory Directory
    {
        get
        {
            if (_directory is null)
            {
                throw new InvalidOperationException("Lucene index has not been initialised.");
            }

            return _directory;
        }
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_directory is not null)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_directory is not null)
            {
                return;
            }

            var path = ResolveIndexPath();
            Directory.CreateDirectory(path);
            _directory = FSDirectory.Open(new DirectoryInfo(path));
            _analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND,
            };
            _writer = new IndexWriter(_directory, config);
            _logger.LogInformation("Lucene index initialised at {Path}", path);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task IndexAsync(SearchDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var luceneDocument = CreateDocument(document);
            var term = new Term(SearchFieldNames.Id, document.FileId.ToString("D", CultureInfo.InvariantCulture));
            _writer!.UpdateDocument(term, luceneDocument);
            _writer.Commit();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task DeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var term = new Term(SearchFieldNames.Id, fileId.ToString("D", CultureInfo.InvariantCulture));
            _writer!.DeleteDocuments(term);
            _writer.Commit();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public DirectoryReader OpenReader()
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("Lucene index has not been initialised.");
        }

        return DirectoryReader.Open(_writer, applyAllDeletes: true);
    }

    private static Document CreateDocument(SearchDocument document)
    {
        var metadataText = document.MetadataText ?? string.Empty;
        var metadataJson = document.MetadataJson ?? string.Empty;
        var author = document.Author ?? string.Empty;
        var title = document.Title ?? string.Empty;
        var fileName = document.FileName ?? string.Empty;
        var mime = document.Mime ?? string.Empty;

        var catchAll = string.Join(" ", new[]
        {
            title,
            author,
            fileName,
            metadataText,
            metadataJson,
            mime,
        });

        var doc = new Document
        {
            new StringField(SearchFieldNames.Id, document.FileId.ToString("D", CultureInfo.InvariantCulture), Field.Store.YES),
            new StringField(SearchFieldNames.Mime, mime, Field.Store.YES),
            new StringField(SearchFieldNames.FileName, fileName, Field.Store.YES),
            new StoredField(SearchFieldNames.Metadata, metadataJson),
            new StoredField(SearchFieldNames.ContentHash, document.ContentHash ?? string.Empty),
            new StoredField(SearchFieldNames.CreatedUtc, document.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)),
            new StoredField(SearchFieldNames.ModifiedUtc, document.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture)),
            new StoredField(SearchFieldNames.MetadataTextStored, metadataText),
            new TextField(SearchFieldNames.CatchAll, catchAll, Field.Store.NO),
            new TextField(SearchFieldNames.MetadataText, metadataText, Field.Store.YES),
            new TextField(SearchFieldNames.Title, title, Field.Store.YES),
            new TextField(SearchFieldNames.Author, author, Field.Store.YES),
        };

        doc.Add(new TextField(SearchFieldNames.FileNameSearch, fileName, Field.Store.NO));
        doc.Add(new TextField(SearchFieldNames.MimeSearch, mime, Field.Store.NO));
        doc.Add(new Int64Field(SearchFieldNames.ModifiedTicks, document.ModifiedUtc.UtcTicks, Field.Store.YES));
        doc.Add(new Int64Field(SearchFieldNames.CreatedTicks, document.CreatedUtc.UtcTicks, Field.Store.YES));
        doc.Add(new Int64Field(SearchFieldNames.SizeBytes, document.SizeBytes, Field.Store.YES));
        doc.Add(new NumericDocValuesField(SearchFieldNames.ModifiedTicksSort, document.ModifiedUtc.UtcTicks));
        doc.Add(new NumericDocValuesField(SearchFieldNames.SizeBytesSort, document.SizeBytes));

        return doc;
    }

    private string ResolveIndexPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.LuceneIndexPath))
        {
            return _options.LuceneIndexPath!;
        }

        if (!string.IsNullOrWhiteSpace(_options.DbPath))
        {
            var dbDirectory = Path.GetDirectoryName(_options.DbPath);
            var baseDirectory = string.IsNullOrWhiteSpace(dbDirectory)
                ? AppContext.BaseDirectory
                : dbDirectory!;
            var databaseName = Path.GetFileNameWithoutExtension(_options.DbPath);
            var folder = string.IsNullOrWhiteSpace(databaseName)
                ? "lucene-index"
                : databaseName + "-lucene";
            return Path.Combine(baseDirectory, folder);
        }

        return Path.Combine(AppContext.BaseDirectory, "lucene-index");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer?.Dispose();
        _directory?.Dispose();
        _analyzer?.Dispose();
        _initializationLock.Dispose();
        _operationLock.Dispose();
    }

}
