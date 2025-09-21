using MediatR;
using Veriado.Application.Common;

namespace Veriado.Application.UseCases.Maintenance;

/// <summary>
/// Requests a complete reindex of the corpus after a search schema upgrade.
/// </summary>
public sealed record ReindexCorpusAfterSchemaUpgradeCommand(
    int TargetSchemaVersion,
    bool ExtractContent = true,
    bool AllowDeferredIndexing = false) : IRequest<AppResult<int>>;
