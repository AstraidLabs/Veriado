using System.IO;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides a Lucene analyzer that performs lowercase conversion and ASCII folding without stop-word removal.
/// </summary>
internal sealed class LuceneMetadataAnalyzer : Analyzer
{
    /// <inheritdoc />
    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        var tokenizer = new StandardTokenizer(LuceneVersion.LUCENE_48, reader);
        TokenStream tokenStream = new LowerCaseFilter(LuceneVersion.LUCENE_48, tokenizer);
        tokenStream = new ASCIIFoldingFilter(tokenStream, preserveOriginal: false);
        return new TokenStreamComponents(tokenizer, tokenStream);
    }
}
