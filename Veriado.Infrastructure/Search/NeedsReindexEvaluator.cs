namespace Veriado.Infrastructure.Search;

public sealed class NeedsReindexEvaluator : INeedsReindexEvaluator
{
    private readonly ISearchIndexSignatureCalculator _signatureCalculator;

    public NeedsReindexEvaluator(ISearchIndexSignatureCalculator signatureCalculator)
    {
        _signatureCalculator = signatureCalculator ?? throw new ArgumentNullException(nameof(signatureCalculator));
    }

    public Task<bool> NeedsReindexAsync(FileEntity file, SearchIndexState state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(state);
        ct.ThrowIfCancellationRequested();

        var signature = _signatureCalculator.Compute(file);
        var normalizedTitle = signature.NormalizedTitle;
        var analyzerVersion = signature.AnalyzerVersion;
        var tokenHash = signature.TokenHash;
        var contentHash = file.Content.Hash.Value;

        var needsReindex = !string.Equals(state.IndexedContentHash, contentHash, StringComparison.Ordinal)
            || !string.Equals(state.IndexedTitle, normalizedTitle, StringComparison.Ordinal)
            || !string.Equals(state.AnalyzerVersion, analyzerVersion, StringComparison.Ordinal)
            || !string.Equals(state.TokenHash, tokenHash, StringComparison.Ordinal);

        return Task.FromResult(needsReindex);
    }
}
