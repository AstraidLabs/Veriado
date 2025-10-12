namespace Veriado.Infrastructure.Search;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Search;

/// <summary>
/// No-op spell suggestion service used when trigram support is disabled.
/// </summary>
internal sealed class SpellSuggestionService : ISpellSuggestionService
{
    private readonly ILogger<SpellSuggestionService> _logger;

    public SpellSuggestionService(ILogger<SpellSuggestionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<SpellSuggestion>> SuggestAsync(
        string token,
        string? language,
        int limit,
        double threshold,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Spell suggestions are disabled because trigram indexing is no longer available.");
        return Task.FromResult<IReadOnlyList<SpellSuggestion>>(Array.Empty<SpellSuggestion>());
    }
}
