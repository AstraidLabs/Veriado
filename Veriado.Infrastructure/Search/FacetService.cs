namespace Veriado.Infrastructure.Search;

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Implements aggregation-based facets using SQLite group by projections.
/// </summary>
internal sealed class FacetService : IFacetService
{
    private const long TenMegabytes = 10L * 1024 * 1024;
    private const long HundredMegabytes = 100L * 1024 * 1024;

    private readonly IDbContextFactory<ReadOnlyDbContext> _contextFactory;
    private readonly ISearchTelemetry _telemetry;
    private readonly ILogger<FacetService> _logger;

    public FacetService(
        IDbContextFactory<ReadOnlyDbContext> contextFactory,
        ISearchTelemetry telemetry,
        ILogger<FacetService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<FacetValue>>> GetFacetsAsync(
        FacetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Fields.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<FacetValue>>(StringComparer.OrdinalIgnoreCase);
        }

        var stopwatch = Stopwatch.StartNew();
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<FileEntity> baseQuery = context.Files.AsNoTracking();
        baseQuery = ApplyFilters(baseQuery, request.Filters);

        var result = new Dictionary<string, IReadOnlyList<FacetValue>>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in request.Fields)
        {
            var key = field.Field.ToLowerInvariant();
            IReadOnlyList<FacetValue> values = field.Kind switch
            {
                FacetKind.Term => await ComputeTermFacetAsync(baseQuery, key, cancellationToken).ConfigureAwait(false),
                FacetKind.DateHistogram => await ComputeDateHistogramAsync(baseQuery, key, field.Interval, cancellationToken)
                    .ConfigureAwait(false),
                FacetKind.NumericRange => await ComputeNumericFacetAsync(baseQuery, key, cancellationToken)
                    .ConfigureAwait(false),
                _ => Array.Empty<FacetValue>(),
            };

            result[field.Field] = values;
        }

        stopwatch.Stop();
        _telemetry.RecordFacetComputation(stopwatch.Elapsed);
        _logger.LogDebug("Computed {FacetCount} facet groups in {Elapsed} ms", result.Count, stopwatch.Elapsed.TotalMilliseconds);
        return result;
    }

    private static IQueryable<FileEntity> ApplyFilters(
        IQueryable<FileEntity> query,
        IReadOnlyCollection<FacetFilter>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return query;
        }

        foreach (var filter in filters)
        {
            switch (filter)
            {
                case TermFacetFilter termFilter when termFilter.Terms.Count > 0:
                    {
                        var terms = termFilter.Terms
                            .Select(static term => term?.Trim().ToLowerInvariant())
                            .Where(static term => !string.IsNullOrWhiteSpace(term))
                            .Distinct()
                            .ToArray();

                        if (terms.Length == 0)
                        {
                            continue;
                        }

                        query = termFilter.Field.ToLowerInvariant() switch
                        {
                            "mime" => query.Where(file => terms.Contains(file.Mime.Value.ToLower())),
                            "author" => query.Where(file => terms.Contains(file.Author.ToLower())),
                            _ => query,
                        };
                        break;
                    }
                case RangeFacetFilter rangeFilter:
                    {
                        var field = rangeFilter.Field.ToLowerInvariant();
                        if (field is "modified" or "modified_utc")
                        {
                            var column = nameof(FileEntity.LastModifiedUtc);
                            query = ApplyDateRangeFilter(query, column, rangeFilter.From, rangeFilter.To);
                        }
                        else if (field is "created" or "created_utc")
                        {
                            var column = nameof(FileEntity.CreatedUtc);
                            query = ApplyDateRangeFilter(query, column, rangeFilter.From, rangeFilter.To);
                        }
                        else if (field is "size" or "size_bytes")
                        {
                            query = ApplyNumericRangeFilter(query, rangeFilter.From, rangeFilter.To);
                        }

                        break;
                    }
            }
        }

        return query;
    }

    private static IQueryable<FileEntity> ApplyDateRangeFilter(
        IQueryable<FileEntity> query,
        string property,
        object? from,
        object? to)
    {
        if (TryConvertDateTime(from, out var lower))
        {
            var lowerIso = lower.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            query = query.Where(file => string.Compare(
                EF.Property<string>(file, property),
                lowerIso,
                StringComparison.Ordinal) >= 0);
        }

        if (TryConvertDateTime(to, out var upper))
        {
            var upperIso = upper.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            query = query.Where(file => string.Compare(
                EF.Property<string>(file, property),
                upperIso,
                StringComparison.Ordinal) <= 0);
        }

        return query;
    }

    private static IQueryable<FileEntity> ApplyNumericRangeFilter(
        IQueryable<FileEntity> query,
        object? from,
        object? to)
    {
        if (TryConvertLong(from, out var lower))
        {
            query = query.Where(file => file.Content != null
                && file.Content.Size.Value >= lower);
        }

        if (TryConvertLong(to, out var upper))
        {
            query = query.Where(file => file.Content != null
                && file.Content.Size.Value <= upper);
        }

        return query;
    }

    private static async Task<IReadOnlyList<FacetValue>> ComputeTermFacetAsync(
        IQueryable<FileEntity> query,
        string field,
        CancellationToken cancellationToken)
    {
        IQueryable<(string Value, long Count)> aggregation = field switch
        {
            "mime" => query
                .GroupBy(file => file.Mime.Value.ToLower())
                .Select(group => new ValueTuple<string, long>(group.Key, group.LongCount())),
            "author" => query
                .GroupBy(file => file.Author.ToLower())
                .Select(group => new ValueTuple<string, long>(group.Key, group.LongCount())),
            _ => null!,
        };

        if (aggregation is null)
        {
            return Array.Empty<FacetValue>();
        }

        var items = await aggregation
            .Where(tuple => tuple.Value != null)
            .OrderByDescending(tuple => tuple.Count)
            .Take(20)
            .Select(tuple => new FacetValue(tuple.Value, tuple.Count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

    private static async Task<IReadOnlyList<FacetValue>> ComputeDateHistogramAsync(
        IQueryable<FileEntity> query,
        string field,
        string? interval,
        CancellationToken cancellationToken)
    {
        var format = interval?.ToLowerInvariant() switch
        {
            "week" => "%Y-%W",
            "month" => "%Y-%m",
            _ => "%Y-%m-%d",
        };

        string property = field switch
        {
            "created" or "created_utc" => nameof(FileEntity.CreatedUtc),
            "modified" or "modified_utc" => nameof(FileEntity.LastModifiedUtc),
            _ => nameof(FileEntity.CreatedUtc),
        };

        var histogram = await query
            .Select(file => new
            {
                Bucket = EF.Functions.Strftime(
                    format,
                    EF.Property<string>(file, property)),
            })
            .GroupBy(x => x.Bucket)
            .Select(group => new FacetValue(group.Key ?? "n/a", group.LongCount()))
            .OrderByDescending(value => value.Count)
            .Take(31)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return histogram;
    }

    private static async Task<IReadOnlyList<FacetValue>> ComputeNumericFacetAsync(
        IQueryable<FileEntity> query,
        string field,
        CancellationToken cancellationToken)
    {
        if (field is not ("size" or "size_bytes"))
        {
            return Array.Empty<FacetValue>();
        }

        var buckets = await query
            .Select(file => file.Content != null ? file.Content.Size.Value : 0L)
            .GroupBy(size => size < TenMegabytes
                ? "0-10MB"
                : size < HundredMegabytes
                    ? "10-100MB"
                    : ">100MB")
            .Select(group => new FacetValue(group.Key, group.LongCount()))
            .OrderByDescending(value => value.Count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return buckets;
    }

    private static bool TryConvertDateTime(object? value, out DateTimeOffset result)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                result = dto;
                return true;
            case DateTime dt:
                result = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
                return true;
            case string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed):
                result = parsed;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryConvertLong(object? value, out long result)
    {
        switch (value)
        {
            case long l:
                result = l;
                return true;
            case int i:
                result = i;
                return true;
            case string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
