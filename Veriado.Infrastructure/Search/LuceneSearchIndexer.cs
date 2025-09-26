using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Veriado.Domain.Search;
using Veriado.Infrastructure.Persistence.Options;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides Lucene.Net backed indexing for search documents.
/// </summary>
internal sealed class LuceneSearchIndexer
{
    private const string GuidFormat = "N";

    private readonly InfrastructureOptions _options;
    private readonly Analyzer? _analyzer;
    private readonly FSDirectory? _directory;
    private readonly object _syncRoot = new();

    public LuceneSearchIndexer(InfrastructureOptions options)
    {
        _options = options;
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

        _analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        _directory = FSDirectory.Open(directoryInfo);

        // Ensure the index exists so that query services can open it without throwing.
        using var writer = CreateWriter();
        writer.Commit();
    }

    public bool IsEnabled => _options.EnableLuceneIntegration && _directory is not null && _analyzer is not null;

    public Task IndexAsync(SearchDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            using var writer = CreateWriter();
            var luceneDocument = BuildDocument(document);
            writer.UpdateDocument(new Term(LuceneSearchFields.Id, document.FileId.ToString(GuidFormat, CultureInfo.InvariantCulture)), luceneDocument);
            writer.Commit();
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            using var writer = CreateWriter();
            writer.DeleteDocuments(new Term(LuceneSearchFields.Id, fileId.ToString(GuidFormat, CultureInfo.InvariantCulture)));
            writer.Commit();
        }

        return Task.CompletedTask;
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

    private static Document BuildDocument(SearchDocument document)
    {
        var luceneDocument = new Document
        {
            new StringField(LuceneSearchFields.Id, document.FileId.ToString(GuidFormat, CultureInfo.InvariantCulture), Field.Store.YES),
            new TextField(LuceneSearchFields.Title, document.Title ?? string.Empty, Field.Store.YES),
            new StringField(LuceneSearchFields.Mime, document.Mime ?? string.Empty, Field.Store.YES),
            new TextField(LuceneSearchFields.Author, document.Author ?? string.Empty, Field.Store.YES),
            new StringField(LuceneSearchFields.Created, document.CreatedUtc.ToString("O", CultureInfo.InvariantCulture), Field.Store.YES),
            new StringField(LuceneSearchFields.Modified, document.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture), Field.Store.YES),
            new TextField(LuceneSearchFields.Text, BuildCombinedText(document), Field.Store.NO),
        };

        return luceneDocument;
    }

    private static string BuildCombinedText(SearchDocument document)
    {
        var parts = new[] { document.Title, document.Author, document.Mime };
        return string.Join(' ', parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }
}
