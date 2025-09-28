namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Requests a complete reindex of the corpus after a search schema upgrade.
/// </summary>
public sealed record ReindexCorpusAfterSchemaUpgradeCommand(
    int TargetSchemaVersion,
    bool AllowDeferredIndexing = false) : IRequest<AppResult<int>>;
