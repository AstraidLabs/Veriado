namespace Veriado.Infrastructure.Search;

/// <summary>
/// Loads and caches synonym definitions for query-time expansion.
/// </summary>
internal sealed class SynonymService : ISynonymProvider
{
    private readonly IDbContextFactory<ReadOnlyDbContext> _contextFactory;
    private readonly ILogger<SynonymService> _logger;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, Dictionary<string, string[]>> _languageCache = new(StringComparer.OrdinalIgnoreCase);

    public SynonymService(IDbContextFactory<ReadOnlyDbContext> contextFactory, ILogger<SynonymService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<string> Expand(string language, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Array.Empty<string>();
        }

        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();
        Dictionary<string, string[]> map;
        lock (_syncRoot)
        {
            if (!_languageCache.TryGetValue(lang, out map!))
            {
                map = LoadLanguage(lang);
                _languageCache[lang] = map;
            }
        }

        if (map.TryGetValue(term, out var direct))
        {
            return direct;
        }

        var lowered = term.Trim().ToLowerInvariant();
        if (map.TryGetValue(lowered, out var normalized))
        {
            return normalized;
        }

        return new[] { lowered };
    }

    private Dictionary<string, string[]> LoadLanguage(string language)
    {
        _logger.LogDebug("Loading synonym map for language {Language}", language);
        using var context = _contextFactory.CreateDbContext();
        var entries = context.Synonyms
            .AsNoTracking()
            .Where(entry => entry.Language == language)
            .Select(entry => new { entry.Term, entry.Variant })
            .ToList();

        if (entries.Count == 0)
        {
            _logger.LogDebug("No synonyms registered for language {Language}", language);
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        var buckets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var canonical = Normalize(entry.Term);
            if (canonical.Length == 0)
            {
                continue;
            }

            if (!buckets.TryGetValue(canonical, out var list))
            {
                list = new List<string> { canonical };
                buckets[canonical] = list;
            }

            var variant = Normalize(entry.Variant);
            if (variant.Length == 0)
            {
                continue;
            }

            if (!list.Contains(variant, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(variant);
            }
        }

        _logger.LogInformation("Loaded {Count} synonym groups for {Language}", buckets.Count, language);
        return buckets.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant();
    }
}
