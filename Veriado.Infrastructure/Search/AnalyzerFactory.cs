using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Veriado.Appl.Search;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides access to configured analyzers and their profiles.
/// </summary>
public sealed class AnalyzerFactory : IAnalyzerFactory
{
    private readonly AnalyzerOptions _options;
    private readonly ConcurrentDictionary<string, ITextAnalyzer> _analyzers;
    private readonly Dictionary<string, AnalyzerProfile> _profiles;

    /// <summary>
    /// Initialises a new instance of the <see cref="AnalyzerFactory"/> class.
    /// </summary>
    public AnalyzerFactory(IOptions<AnalyzerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _profiles = BuildProfileMap(_options);
        _analyzers = new ConcurrentDictionary<string, ITextAnalyzer>(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ITextAnalyzer Create(string? profileOrLang = null)
    {
        var profile = ResolveProfile(profileOrLang);
        return _analyzers.GetOrAdd(profile.Name, _ => new GeneralTextAnalyzer(CloneProfile(profile)));
    }

    /// <inheritdoc />
    public bool TryGetProfile(string profileOrLang, out AnalyzerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profileOrLang))
        {
            profile = CloneProfile(ResolveProfile(null));
            return true;
        }

        if (_profiles.TryGetValue(profileOrLang, out var existing))
        {
            profile = CloneProfile(existing);
            return true;
        }

        profile = null!;
        return false;
    }

    private AnalyzerProfile ResolveProfile(string? profileOrLang)
    {
        var key = string.IsNullOrWhiteSpace(profileOrLang) ? _options.DefaultProfile : profileOrLang!;
        if (_profiles.TryGetValue(key, out var profile))
        {
            return profile;
        }

        if (_profiles.TryGetValue(_options.DefaultProfile, out var fallback))
        {
            return fallback;
        }

        return _profiles.Values.First();
    }

    private static Dictionary<string, AnalyzerProfile> BuildProfileMap(AnalyzerOptions options)
    {
        var map = new Dictionary<string, AnalyzerProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in options.Profiles)
        {
            if (pair.Value is null)
            {
                continue;
            }

            var profile = CloneProfile(pair.Key, pair.Value);
            map[profile.Name] = profile;
        }

        if (map.Count == 0)
        {
            var profile = new AnalyzerProfile { Name = options.DefaultProfile };
            map[profile.Name] = profile;
        }
        else if (!map.ContainsKey(options.DefaultProfile))
        {
            map[options.DefaultProfile] = new AnalyzerProfile { Name = options.DefaultProfile };
        }

        return map;
    }

    private static AnalyzerProfile CloneProfile(AnalyzerProfile profile)
    {
        return new AnalyzerProfile
        {
            Name = profile.Name,
            EnableStemming = profile.EnableStemming,
            KeepNumbers = profile.KeepNumbers,
            Stopwords = profile.Stopwords?.ToArray() ?? Array.Empty<string>(),
            SplitFilenames = profile.SplitFilenames,
            CustomTokenizer = profile.CustomTokenizer,
            CustomFilters = profile.CustomFilters?.ToArray() ?? Array.Empty<string>(),
        };
    }

    private static AnalyzerProfile CloneProfile(string key, AnalyzerProfile profile)
    {
        var name = string.IsNullOrWhiteSpace(profile.Name) ? key : profile.Name;
        return new AnalyzerProfile
        {
            Name = name,
            EnableStemming = profile.EnableStemming,
            KeepNumbers = profile.KeepNumbers,
            Stopwords = profile.Stopwords?.ToArray() ?? Array.Empty<string>(),
            SplitFilenames = profile.SplitFilenames,
            CustomTokenizer = profile.CustomTokenizer,
            CustomFilters = profile.CustomFilters?.ToArray() ?? Array.Empty<string>(),
        };
    }
}
