using System;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Application.Abstractions;
using Veriado.Domain.Files;

namespace Veriado.Application.Files.Handlers;

internal static class FileIndexingHelper
{
    public static async Task<bool> ReindexAsync(
        FileEntity file,
        ITextExtractor textExtractor,
        ISearchIndexer searchIndexer,
        CancellationToken cancellationToken)
    {
        var text = await textExtractor.ExtractAsync(file, cancellationToken).ConfigureAwait(false);
        var document = file.ToSearchDocument(text);
        await searchIndexer.UpsertAsync(document, cancellationToken).ConfigureAwait(false);
        file.SearchIndex.ApplyIndexed(
            file.SearchIndex.SchemaVersion,
            DateTimeOffset.UtcNow,
            file.Content.Hash.Value,
            document.Title);
        return true;
    }
}
