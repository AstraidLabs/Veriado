using System;
using System.Linq;
using AutoMapper.QueryableExtensions;
using Veriado.Appl.Search;
using Veriado.Appl.Search.Abstractions;
using Veriado.Contracts.Common;

namespace Veriado.Appl.UseCases.Queries.FileGrid;

/// <summary>
/// Handles the advanced file grid query.
/// </summary>
public sealed class FileGridQueryHandler : IRequestHandler<FileGridQuery, PageResult<FileSummaryDto>>
{
    private const int CandidateBatchSize = 900;

    private readonly IReadOnlyFileContextFactory _contextFactory;
    private readonly ISearchQueryService _searchQueryService;
    private readonly ISearchHistoryService _historyService;
    private readonly ISearchFavoritesService _favoritesService;
    private readonly IClock _clock;
    private readonly FileGridQueryOptions _options;
    private readonly IMapper _mapper;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly ITrigramQueryBuilder _trigramBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileGridQueryHandler"/> class.
    /// </summary>
    public FileGridQueryHandler(
        IReadOnlyFileContextFactory contextFactory,
        ISearchQueryService searchQueryService,
        ISearchHistoryService historyService,
        ISearchFavoritesService favoritesService,
        IClock clock,
        FileGridQueryOptions options,
        IMapper mapper,
        IAnalyzerFactory analyzerFactory,
        ITrigramQueryBuilder trigramBuilder)
    {
        _contextFactory = contextFactory;
        _searchQueryService = searchQueryService;
        _historyService = historyService;
        _favoritesService = favoritesService;
        _clock = clock;
        _options = options;
        _mapper = mapper;
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _trigramBuilder = trigramBuilder ?? throw new ArgumentNullException(nameof(trigramBuilder));
    }

    /// <inheritdoc />
    public async Task<PageResult<FileSummaryDto>> Handle(FileGridQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dto = request.Parameters;
        var pageNumber = Math.Max(dto.Page, 1);
        var pageSize = Math.Clamp(dto.PageSize, 1, _options.MaxPageSize);
        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        SearchFavoriteItem? favorite = null;
        if (!string.IsNullOrWhiteSpace(dto.SavedQueryKey))
        {
            favorite = await _favoritesService.TryGetByKeyAsync(dto.SavedQueryKey!, cancellationToken).ConfigureAwait(false);
        }

        var queryText = dto.Text;
        string? fuzzyMatchQuery = null;
        var fuzzyRequestedByFavorite = favorite?.IsFuzzy ?? false;
        var fuzzyRequestedByDto = false;

        if (favorite is not null)
        {
            queryText ??= favorite.QueryText;
            if (favorite.IsFuzzy)
            {
                fuzzyMatchQuery = favorite.MatchQuery;
            }
        }

        if (dto.Fuzzy && !string.IsNullOrWhiteSpace(dto.Text))
        {
            var normalizedFuzzy = NormalizeForTrigram(dto.Text!);
            if (_trigramBuilder.TryBuild(normalizedFuzzy, dto.TextAllTerms, out var builtFuzzy))
            {
                fuzzyMatchQuery = builtFuzzy;
                fuzzyRequestedByDto = true;
            }
        }

        queryText ??= dto.Text;
        var matchQuery = favorite is { IsFuzzy: true } ? null : favorite?.MatchQuery;

        if (!string.IsNullOrWhiteSpace(dto.Text) && string.IsNullOrWhiteSpace(matchQuery) && !(favorite?.IsFuzzy ?? false))
        {
            if (FtsQueryBuilder.TryBuild(dto.Text!, dto.TextPrefix, dto.TextAllTerms, _analyzerFactory, out var built))
            {
                matchQuery = built;
            }
        }

        await using var context = await _contextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        var filesQuery = context.Files;

        var filteredQuery = QueryableFilters.ApplyFilters(filesQuery, dto, today);
        var sortSpecifications = dto.Sort ?? new List<FileSortSpecDto>();
        bool sortByScore = sortSpecifications.Count > 0
            && string.Equals(sortSpecifications[0].Field, "score", StringComparison.OrdinalIgnoreCase);

        var fuzzyMode = (fuzzyRequestedByFavorite || fuzzyRequestedByDto) && !string.IsNullOrWhiteSpace(fuzzyMatchQuery);

        if (fuzzyMode)
        {
            var match = fuzzyMatchQuery!;
            var plan = SearchQueryPlanFactory.FromTrigram(match, queryText);
            var candidates = await _searchQueryService
                .SearchFuzzyWithScoresAsync(plan, 0, _options.MaxCandidateResults, cancellationToken)
                .ConfigureAwait(false);

            if (candidates.Count == 0)
            {
                await _historyService.AddAsync(queryText, match, 0, true, cancellationToken).ConfigureAwait(false);
                return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
            }

            var candidateScores = new Dictionary<Guid, double>(candidates.Count);
            foreach (var (id, score) in candidates)
            {
                candidateScores[id] = score;
            }

            var candidateIds = candidates.Select(static candidate => candidate.Id).ToArray();
            var matchingIds = await CollectMatchingIdsAsync(context, filteredQuery, candidateIds, cancellationToken)
                .ConfigureAwait(false);

            if (matchingIds.Count == 0)
            {
                await _historyService.AddAsync(queryText, match, 0, true, cancellationToken).ConfigureAwait(false);
                return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
            }

            if (sortByScore)
            {
                var orderedIds = new List<Guid>(matchingIds.Count);
                foreach (var (id, _) in candidates)
                {
                    if (matchingIds.Contains(id))
                    {
                        orderedIds.Add(id);
                    }
                }

                var totalCount = orderedIds.Count;
                var skip = (pageNumber - 1) * pageSize;
                var pageIds = orderedIds.Skip(skip).Take(pageSize).ToArray();

                if (pageIds.Length == 0)
                {
                    await _historyService.AddAsync(queryText, match, totalCount, true, cancellationToken).ConfigureAwait(false);
                    return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, totalCount);
                }

                var summaries = await FetchSummariesAsync(context, filteredQuery, pageIds, cancellationToken)
                    .ConfigureAwait(false);
                var items = new List<FileSummaryDto>(pageIds.Length);
                foreach (var id in pageIds)
                {
                    if (summaries.TryGetValue(id, out var summary))
                    {
                        if (candidateScores.TryGetValue(id, out var score))
                        {
                            items.Add(summary with { Score = score });
                        }
                        else
                        {
                            items.Add(summary);
                        }
                    }
                }

                await _historyService.AddAsync(queryText, match, totalCount, true, cancellationToken).ConfigureAwait(false);
                return new PageResult<FileSummaryDto>(items, pageNumber, pageSize, totalCount);
            }

            var summaryMap = await FetchSummariesAsync(context, filteredQuery, matchingIds, cancellationToken)
                .ConfigureAwait(false);
            if (summaryMap.Count == 0)
            {
                await _historyService.AddAsync(queryText, match, 0, true, cancellationToken).ConfigureAwait(false);
                return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
            }

            var enrichedSummaries = new List<FileSummaryDto>(summaryMap.Count);
            foreach (var summary in summaryMap.Values)
            {
                if (candidateScores.TryGetValue(summary.Id, out var score))
                {
                    enrichedSummaries.Add(summary with { Score = score });
                }
                else
                {
                    enrichedSummaries.Add(summary);
                }
            }

            var orderedItems = OrderSummaries(enrichedSummaries, sortSpecifications);
            var totalCountNonScore = orderedItems.Count;
            var skipCount = (pageNumber - 1) * pageSize;
            var pageItems = orderedItems.Skip(skipCount).Take(pageSize).ToList();

            await _historyService.AddAsync(queryText, match, totalCountNonScore, true, cancellationToken).ConfigureAwait(false);
            return new PageResult<FileSummaryDto>(pageItems, pageNumber, pageSize, totalCountNonScore);
        }

        if ((fuzzyRequestedByFavorite || fuzzyRequestedByDto) && !fuzzyMode)
        {
            return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
        }

        if (!string.IsNullOrWhiteSpace(matchQuery))
        {
            var match = matchQuery!;
            var plan = SearchQueryPlanFactory.FromMatch(match, queryText);
            var candidates = await _searchQueryService
                .SearchWithScoresAsync(plan, 0, _options.MaxCandidateResults, cancellationToken)
                .ConfigureAwait(false);

            if (candidates.Count == 0)
            {
                await _historyService.AddAsync(queryText, match, 0, false, cancellationToken).ConfigureAwait(false);
                return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
            }

            var candidateScores = new Dictionary<Guid, double>(candidates.Count);
            foreach (var (id, score) in candidates)
            {
                candidateScores[id] = score;
            }

            var candidateIds = candidates.Select(static candidate => candidate.Id).ToArray();
            var matchingIds = await CollectMatchingIdsAsync(context, filteredQuery, candidateIds, cancellationToken)
                .ConfigureAwait(false);

            if (matchingIds.Count == 0)
            {
                await _historyService.AddAsync(queryText, match, 0, false, cancellationToken).ConfigureAwait(false);
                return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
            }

            if (sortByScore)
            {
                var orderedIds = new List<Guid>(matchingIds.Count);
                foreach (var (id, _) in candidates)
                {
                    if (matchingIds.Contains(id))
                    {
                        orderedIds.Add(id);
                    }
                }

                var totalCount = orderedIds.Count;
                var skip = (pageNumber - 1) * pageSize;
                var pageIds = orderedIds.Skip(skip).Take(pageSize).ToArray();

                if (pageIds.Length == 0)
                {
                    await _historyService.AddAsync(queryText, match, totalCount, false, cancellationToken).ConfigureAwait(false);
                    return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, totalCount);
                }

                var summaries = await FetchSummariesAsync(context, filteredQuery, pageIds, cancellationToken)
                    .ConfigureAwait(false);
                var items = new List<FileSummaryDto>(pageIds.Length);
                foreach (var id in pageIds)
                {
                    if (summaries.TryGetValue(id, out var summary))
                    {
                        if (candidateScores.TryGetValue(id, out var score))
                        {
                            items.Add(summary with { Score = score });
                        }
                        else
                        {
                            items.Add(summary);
                        }
                    }
                }

                await _historyService.AddAsync(queryText, match, totalCount, false, cancellationToken).ConfigureAwait(false);
                return new PageResult<FileSummaryDto>(items, pageNumber, pageSize, totalCount);
            }

            var summaryMap = await FetchSummariesAsync(context, filteredQuery, matchingIds, cancellationToken)
                .ConfigureAwait(false);
            if (summaryMap.Count == 0)
            {
                await _historyService.AddAsync(queryText, match, 0, false, cancellationToken).ConfigureAwait(false);
                return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
            }

            var enrichedSummaries = new List<FileSummaryDto>(summaryMap.Count);
            foreach (var summary in summaryMap.Values)
            {
                if (candidateScores.TryGetValue(summary.Id, out var score))
                {
                    enrichedSummaries.Add(summary with { Score = score });
                }
                else
                {
                    enrichedSummaries.Add(summary);
                }
            }

            var orderedItems = OrderSummaries(enrichedSummaries, sortSpecifications);
            var totalCountNonScore = orderedItems.Count;
            var skipCount = (pageNumber - 1) * pageSize;
            var pageItems = orderedItems.Skip(skipCount).Take(pageSize).ToList();

            await _historyService.AddAsync(queryText, match, totalCountNonScore, false, cancellationToken).ConfigureAwait(false);
            return new PageResult<FileSummaryDto>(pageItems, pageNumber, pageSize, totalCountNonScore);
        }

        var total = await context.CountAsync(filteredQuery, cancellationToken).ConfigureAwait(false);
        var orderedQuery = QueryableFilters.ApplyOrdering(filteredQuery, sortSpecifications);
        var offset = (pageNumber - 1) * pageSize;
        var pageQueryProjection = orderedQuery
            .Skip(offset)
            .Take(pageSize)
            .ProjectTo<FileSummaryDto>(_mapper.ConfigurationProvider);

        var projectedItems = await context.ToListAsync(pageQueryProjection, cancellationToken).ConfigureAwait(false);
        return new PageResult<FileSummaryDto>(projectedItems, pageNumber, pageSize, total);
    }

    private static async Task<HashSet<Guid>> CollectMatchingIdsAsync(
        IReadOnlyFileContext context,
        IQueryable<FileEntity> query,
        Guid[] candidateIds,
        CancellationToken cancellationToken)
    {
        var matched = new HashSet<Guid>();
        foreach (var chunk in candidateIds.Chunk(CandidateBatchSize))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            var chunkQuery = query.Where(file => chunk.Contains(file.Id));
            var chunkIds = await context.ToListAsync(chunkQuery.Select(file => file.Id), cancellationToken)
                .ConfigureAwait(false);
            foreach (var id in chunkIds)
            {
                matched.Add(id);
            }
        }

        return matched;
    }

    private async Task<Dictionary<Guid, FileSummaryDto>> FetchSummariesAsync(
        IReadOnlyFileContext context,
        IQueryable<FileEntity> query,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        var summaries = new Dictionary<Guid, FileSummaryDto>();
        foreach (var chunk in ids.Chunk(CandidateBatchSize))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            var chunkQuery = query.Where(file => chunk.Contains(file.Id));
                var chunkSummaries = await context.ToListAsync(
                    chunkQuery.ProjectTo<FileSummaryDto>(_mapper.ConfigurationProvider),
                    cancellationToken)
                    .ConfigureAwait(false);
            foreach (var summary in chunkSummaries)
            {
                summaries[summary.Id] = summary;
            }
        }

        return summaries;
    }

    private static List<FileSummaryDto> OrderSummaries(
        IEnumerable<FileSummaryDto> items,
        IReadOnlyList<FileSortSpecDto> sort)
    {
        var summaries = new List<FileSummaryDto>(items);
        if (summaries.Count <= 1)
        {
            return summaries;
        }

        var activeSorts = sort is { Count: > 0 }
            ? sort.Where(static spec => !string.IsNullOrWhiteSpace(spec.Field)).ToList()
            : new List<FileSortSpecDto>();

        if (activeSorts.Count == 0)
        {
            activeSorts.Add(new FileSortSpecDto { Field = "name" });
        }

        summaries.Sort((left, right) =>
        {
            foreach (var spec in activeSorts)
            {
                var comparison = CompareSummaries(left, right, spec.Field);
                if (comparison != 0)
                {
                    return spec.Descending ? -comparison : comparison;
                }
            }

            return left.Id.CompareTo(right.Id);
        });

        return summaries;
    }

    private string NormalizeForTrigram(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var tokens = TextNormalization
            .Tokenize(text, _analyzerFactory)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        return tokens.Length == 0 ? string.Empty : string.Join(' ', tokens);
    }

    private static int CompareSummaries(FileSummaryDto left, FileSummaryDto right, string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return 0;
        }

        return field.ToLowerInvariant() switch
        {
            "name" => string.Compare(left.Name, right.Name, StringComparison.Ordinal),
            "mime" => string.Compare(left.Mime, right.Mime, StringComparison.Ordinal),
            "extension" => string.Compare(left.Extension, right.Extension, StringComparison.Ordinal),
            "size" => left.Size.CompareTo(right.Size),
            "createdutc" => DateTimeOffset.Compare(left.CreatedUtc, right.CreatedUtc),
            "modifiedutc" => DateTimeOffset.Compare(left.LastModifiedUtc, right.LastModifiedUtc),
            "version" => left.Version.CompareTo(right.Version),
            "author" => string.Compare(left.Author, right.Author, StringComparison.Ordinal),
            "validuntil" => Nullable.Compare(left.Validity?.ValidUntil, right.Validity?.ValidUntil),
            "score" => Nullable.Compare(left.Score, right.Score),
            _ => 0,
        };
    }
}
