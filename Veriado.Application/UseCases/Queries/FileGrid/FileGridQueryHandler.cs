using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AutoMapper.QueryableExtensions;
using Microsoft.Data.Sqlite;
using Veriado.Appl.Search;
using Veriado.Appl.Search.Abstractions;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;

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
        IAnalyzerFactory analyzerFactory)
    {
        _contextFactory = contextFactory;
        _searchQueryService = searchQueryService;
        _historyService = historyService;
        _favoritesService = favoritesService;
        _clock = clock;
        _options = options;
        _mapper = mapper;
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
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
            if (TrigramQueryBuilder.TryBuild(normalizedFuzzy, dto.TextAllTerms, out var builtFuzzy))
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

        var todayReference = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var filteredQuery = QueryableFilters.ApplyFilters(filesQuery, dto, today);
        var sortSpecifications = dto.Sort ?? new List<FileSortSpecDto>();
        bool sortByScore = sortSpecifications.Count > 0
            && string.Equals(sortSpecifications[0].Field, "score", StringComparison.OrdinalIgnoreCase);

        var fuzzyMode = (fuzzyRequestedByFavorite || fuzzyRequestedByDto) && !string.IsNullOrWhiteSpace(fuzzyMatchQuery);

        if (fuzzyMode)
        {
            var match = fuzzyMatchQuery!;
            var plan = ApplyFiltersToPlan(SearchQueryPlanFactory.FromTrigram(match, queryText), dto, today);
            var searchOffset = (pageNumber - 1) * pageSize;
            var candidateTake = Math.Max(pageSize * 2, searchOffset + pageSize);
            candidateTake = Math.Min(candidateTake, _options.MaxCandidateResults);
            IReadOnlyList<(Guid Id, double Score)> candidates;
            var candidateScores = new Dictionary<Guid, double>();
            HashSet<Guid> matchingIds = new();
            var orderedIds = new List<Guid>();

            while (true)
            {
                candidates = await _searchQueryService
                    .SearchFuzzyWithScoresAsync(plan, 0, candidateTake, cancellationToken)
                    .ConfigureAwait(false);

                if (candidates.Count == 0)
                {
                    await _historyService.AddAsync(queryText, match, 0, true, cancellationToken).ConfigureAwait(false);
                    return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
                }

                candidateScores = new Dictionary<Guid, double>(candidates.Count);
                var candidateIds = new Guid[candidates.Count];
                var index = 0;
                foreach (var (id, score) in candidates)
                {
                    candidateScores[id] = score;
                    candidateIds[index++] = id;
                }

                matchingIds = await CollectMatchingIdsAsync(context, filteredQuery, candidateIds, cancellationToken)
                    .ConfigureAwait(false);

                if (matchingIds.Count == 0)
                {
                    if (candidateTake >= _options.MaxCandidateResults)
                    {
                        await _historyService.AddAsync(queryText, match, 0, true, cancellationToken)
                            .ConfigureAwait(false);
                        return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
                    }

                    var nextTake = Math.Min(
                        _options.MaxCandidateResults,
                        Math.Max(candidateTake * 2, searchOffset + (2 * pageSize)));
                    if (nextTake == candidateTake)
                    {
                        await _historyService.AddAsync(queryText, match, 0, true, cancellationToken)
                            .ConfigureAwait(false);
                        return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
                    }

                    candidateTake = nextTake;
                    continue;
                }

                if (sortByScore)
                {
                    orderedIds = new List<Guid>(matchingIds.Count);
                    foreach (var (id, _) in candidates)
                    {
                        if (matchingIds.Contains(id))
                        {
                            orderedIds.Add(id);
                        }
                    }

                    if (orderedIds.Count >= searchOffset + pageSize || candidateTake >= _options.MaxCandidateResults)
                    {
                        break;
                    }

                    var nextTake = Math.Min(
                        _options.MaxCandidateResults,
                        Math.Max(candidateTake * 2, searchOffset + (2 * pageSize)));
                    if (nextTake == candidateTake)
                    {
                        break;
                    }

                    candidateTake = nextTake;
                    continue;
                }

                break;
            }

            if (matchingIds.Count == 0)
            {
                await _historyService.AddAsync(queryText, match, 0, true, cancellationToken).ConfigureAwait(false);
                return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
            }

            if (sortByScore)
            {
                var totalCount = orderedIds.Count;
                var skip = searchOffset;
                var pageIds = orderedIds.Skip(skip).Take(pageSize).ToArray();

                var hasMore = totalCount > skip + pageIds.Length;

                if (pageIds.Length == 0)
                {
                    await _historyService.AddAsync(queryText, match, totalCount, true, cancellationToken).ConfigureAwait(false);
                    return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, totalCount, hasMore);
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
                return new PageResult<FileSummaryDto>(items, pageNumber, pageSize, totalCount, hasMore);
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
            var hasMoreNonScore = totalCountNonScore > skipCount + pageItems.Count;

            await _historyService.AddAsync(queryText, match, totalCountNonScore, true, cancellationToken).ConfigureAwait(false);
            return new PageResult<FileSummaryDto>(pageItems, pageNumber, pageSize, totalCountNonScore, hasMoreNonScore);
        }

        if ((fuzzyRequestedByFavorite || fuzzyRequestedByDto) && !fuzzyMode)
        {
            return new PageResult<FileSummaryDto>(Array.Empty<FileSummaryDto>(), pageNumber, pageSize, 0);
        }

        if (!string.IsNullOrWhiteSpace(matchQuery))
        {
            var match = matchQuery!;
            var searchOffset = (pageNumber - 1) * pageSize;
            var candidateLimit = Math.Max(pageSize * 2, searchOffset + pageSize);
            candidateLimit = Math.Min(candidateLimit, _options.MaxCandidateResults);
            FileGridSearchResult gridResult;

            while (true)
            {
                gridResult = await _searchQueryService
                    .SearchGridAsync(match, dto, sortSpecifications, todayReference, searchOffset, pageSize, candidateLimit, cancellationToken)
                    .ConfigureAwait(false);

                if (gridResult.Items.Count >= pageSize)
                {
                    break;
                }

                if (candidateLimit >= _options.MaxCandidateResults)
                {
                    break;
                }

                if (!gridResult.HasMore)
                {
                    break;
                }

                var doubled = Math.Max(candidateLimit * 2, searchOffset + (2 * pageSize));
                var next = Math.Min(doubled, _options.MaxCandidateResults);
                if (next == candidateLimit)
                {
                    break;
                }

                candidateLimit = next;
            }

            if (candidateLimit >= _options.MaxCandidateResults && gridResult.Items.Count < pageSize)
            {
                var clippedHasMore = searchOffset + pageSize < _options.MaxCandidateResults;
                gridResult = gridResult with { HasMore = gridResult.HasMore || clippedHasMore };
            }

            await _historyService.AddAsync(queryText, match, gridResult.TotalCount, false, cancellationToken).ConfigureAwait(false);
            return new PageResult<FileSummaryDto>(gridResult.Items, pageNumber, pageSize, gridResult.TotalCount, gridResult.HasMore, gridResult.IsTruncated);
        }

        var total = await context.CountAsync(filteredQuery, cancellationToken).ConfigureAwait(false);
        var orderedQuery = QueryableFilters.ApplyOrdering(filteredQuery, sortSpecifications);
        var offset = (pageNumber - 1) * pageSize;
        var pageQueryProjection = orderedQuery
            .Skip(offset)
            .Take(pageSize)
            .ProjectTo<FileSummaryDto>(_mapper.ConfigurationProvider);

        var projectedItems = await context.ToListAsync(pageQueryProjection, cancellationToken).ConfigureAwait(false);
        var hasMorePages = total > offset + projectedItems.Count;
        return new PageResult<FileSummaryDto>(projectedItems, pageNumber, pageSize, total, hasMorePages);
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

    private SearchQueryPlan ApplyFiltersToPlan(SearchQueryPlan plan, FileGridQueryDto dto, DateOnly today)
    {
        if (dto is null)
        {
            return plan;
        }

        var clauses = plan.WhereClauses.Count > 0
            ? new List<string>(plan.WhereClauses)
            : new List<string>();
        var parameters = plan.Parameters.Count > 0
            ? new List<SqliteParameterDefinition>(plan.Parameters)
            : new List<SqliteParameterDefinition>();

        var usedNames = new HashSet<string>(parameters.Select(static parameter => parameter.Name), StringComparer.Ordinal);
        var parameterIndex = 0;
        var todayReference = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        string NextParameterName()
        {
            string candidate;
            do
            {
                candidate = "$fg" + parameterIndex++;
            }
            while (!usedNames.Add(candidate));

            return candidate;
        }

        string AddParameter(object value, SqliteType type)
        {
            var name = NextParameterName();
            parameters.Add(new SqliteParameterDefinition(name, value, type));
            return name;
        }

        void AddLikeClause(string column, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var pattern = $"%{QueryableFilters.EscapeLike(value)}%";
            var parameter = AddParameter(pattern, SqliteType.Text);
            clauses.Add($"{column} LIKE {parameter} ESCAPE '\\'");
        }

        void AddExtensionClause()
        {
            if (string.IsNullOrWhiteSpace(dto.Extension))
            {
                return;
            }

            var trimmed = dto.Extension.Trim();
            var sanitized = trimmed.TrimStart('.');
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return;
            }

            var normalized = sanitized.ToLowerInvariant();
            if (dto.ExtensionMatchMode == ExtensionMatchMode.Contains)
            {
                var pattern = $"%{QueryableFilters.EscapeLike(normalized)}%";
                var parameter = AddParameter(pattern, SqliteType.Text);
                clauses.Add($"LOWER(f.extension) LIKE {parameter} ESCAPE '\\'");
            }
            else
            {
                var parameter = AddParameter(normalized, SqliteType.Text);
                clauses.Add($"LOWER(f.extension) = {parameter}");
            }
        }

        static string FormatDate(DateTimeOffset value)
            => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        AddLikeClause("f.name", dto.Name);
        AddExtensionClause();
        AddLikeClause("f.mime", dto.Mime);
        AddLikeClause("f.author", dto.Author);

        if (dto.IsReadOnly.HasValue)
        {
            var parameter = AddParameter(dto.IsReadOnly.Value ? 1 : 0, SqliteType.Integer);
            clauses.Add($"f.is_read_only = {parameter}");
        }

        if (dto.IsIndexStale.HasValue)
        {
            var parameter = AddParameter(dto.IsIndexStale.Value ? 1 : 0, SqliteType.Integer);
            clauses.Add($"f.fts_is_stale = {parameter}");
        }

        if (dto.SizeMin.HasValue)
        {
            var parameter = AddParameter(dto.SizeMin.Value, SqliteType.Integer);
            clauses.Add($"f.size_bytes >= {parameter}");
        }

        if (dto.SizeMax.HasValue)
        {
            var parameter = AddParameter(dto.SizeMax.Value, SqliteType.Integer);
            clauses.Add($"f.size_bytes <= {parameter}");
        }

        if (dto.CreatedFromUtc.HasValue)
        {
            var parameter = AddParameter(FormatDate(dto.CreatedFromUtc.Value), SqliteType.Text);
            clauses.Add($"f.created_utc >= {parameter}");
        }

        if (dto.CreatedToUtc.HasValue)
        {
            var parameter = AddParameter(FormatDate(dto.CreatedToUtc.Value), SqliteType.Text);
            clauses.Add($"f.created_utc <= {parameter}");
        }

        if (dto.ModifiedFromUtc.HasValue)
        {
            var parameter = AddParameter(FormatDate(dto.ModifiedFromUtc.Value), SqliteType.Text);
            clauses.Add($"f.modified_utc >= {parameter}");
        }

        if (dto.ModifiedToUtc.HasValue)
        {
            var parameter = AddParameter(FormatDate(dto.ModifiedToUtc.Value), SqliteType.Text);
            clauses.Add($"f.modified_utc <= {parameter}");
        }

        if (dto.Version.HasValue)
        {
            var parameter = AddParameter(dto.Version.Value, SqliteType.Integer);
            clauses.Add($"f.version = {parameter}");
        }

        if (dto.HasValidity.HasValue)
        {
            if (dto.HasValidity.Value)
            {
                clauses.Add("EXISTS (SELECT 1 FROM files_validity v WHERE v.file_id = f.id)");
            }
            else
            {
                clauses.Add("NOT EXISTS (SELECT 1 FROM files_validity v WHERE v.file_id = f.id)");
            }
        }

        if (dto.IsCurrentlyValid.HasValue)
        {
            var reference = AddParameter(FormatDate(todayReference), SqliteType.Text);
            if (dto.IsCurrentlyValid.Value)
            {
                clauses.Add($"EXISTS (SELECT 1 FROM files_validity v WHERE v.file_id = f.id AND v.issued_at <= {reference} AND v.valid_until >= {reference})");
            }
            else
            {
                clauses.Add($"NOT EXISTS (SELECT 1 FROM files_validity v WHERE v.file_id = f.id AND v.issued_at <= {reference} AND v.valid_until >= {reference})");
            }
        }

        if (dto.ExpiringInDays.HasValue)
        {
            var horizon = today.AddDays(dto.ExpiringInDays.Value);
            var start = AddParameter(FormatDate(todayReference), SqliteType.Text);
            var end = AddParameter(FormatDate(new DateTimeOffset(horizon.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc))), SqliteType.Text);
            clauses.Add($"EXISTS (SELECT 1 FROM files_validity v WHERE v.file_id = f.id AND v.valid_until >= {start} AND v.valid_until <= {end})");
        }

        if (clauses.Count == plan.WhereClauses.Count && parameters.Count == plan.Parameters.Count)
        {
            return plan;
        }

        return plan with
        {
            WhereClauses = clauses,
            Parameters = parameters,
        };
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
