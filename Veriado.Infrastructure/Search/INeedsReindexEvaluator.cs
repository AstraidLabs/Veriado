namespace Veriado.Infrastructure.Search;

public interface INeedsReindexEvaluator
{
    Task<bool> NeedsReindexAsync(FileEntity file, SearchIndexState state, CancellationToken ct);
}
