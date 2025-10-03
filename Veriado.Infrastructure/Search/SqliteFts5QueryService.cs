using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using Veriado.Appl.Search;
using Veriado.Domain.Search;
using Veriado.Contracts.Files;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Provides query access to the SQLite FTS5 virtual table.
/// </summary>
internal sealed class SqliteFts5QueryService
{
    private const char Ellipsis = '…';
    private const string SearchTableName = "file_search";
    private const string SearchTableAlias = "s";
    private static readonly Encoding Utf8 = Encoding.UTF8;

    private static readonly int[] SnippetPriority =
    {
        0, // title
        2, // author
        3, // metadata_text
        1, // mime
        4, // metadata
    };

    private static readonly IReadOnlyDictionary<int, string> ColumnNameMap = new Dictionary<int, string>
    {
        [0] = "title",
        [1] = "mime",
        [2] = "author",
        [3] = "metadata_text",
        [4] = "metadata",
    };

    private readonly InfrastructureOptions _options;
    private readonly ISearchTelemetry _telemetry;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteFts5QueryService(
        InfrastructureOptions options,
        ISearchTelemetry telemetry,
        IAnalyzerFactory analyzerFactory,
        ISqliteConnectionFactory connectionFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<FileGridSearchResult> SearchGridAsync(
        string? matchQuery,
        FileGridQueryDto parameters,
        IReadOnlyList<FileSortSpecDto> sort,
        DateTimeOffset today,
        int offset,
        int limit,
        int candidateLimit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(sort);

        if (string.IsNullOrWhiteSpace(matchQuery))
        {
            return FileGridSearchResult.Empty;
        }

        if (!_options.IsFulltextAvailable)
        {
            return FileGridSearchResult.Empty;
        }

        if (limit <= 0)
        {
            return FileGridSearchResult.Empty;
        }

        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        var effectiveLimit = Math.Max(limit, 0);
        var effectiveOffset = Math.Max(offset, 0);
        var effectiveCandidateLimit = Math.Max(Math.Max(candidateLimit, effectiveLimit), effectiveOffset + effectiveLimit);
        effectiveCandidateLimit = Math.Min(effectiveCandidateLimit, _options.MaxCandidateResults);

        var sql = BuildGridSql(sort, parameters, today, out var parameterDefinitions);
        command.CommandText = sql;
        command.Parameters.Add("$match", SqliteType.Text).Value = matchQuery;
        command.Parameters.Add("$candidateLimit", SqliteType.Integer).Value = effectiveCandidateLimit;
        command.Parameters.Add("$limit", SqliteType.Integer).Value = effectiveLimit;
        command.Parameters.Add("$offset", SqliteType.Integer).Value = effectiveOffset;

        foreach (var definition in parameterDefinitions)
        {
            var parameter = command.Parameters.Add(definition.Name, definition.Type ?? SqliteType.Text);
            parameter.Value = definition.Value ?? DBNull.Value;
        }

        var stopwatch = Stopwatch.StartNew();
        var items = new List<FileSummaryDto>();
        var windowCount = 0;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var totalOrdinal = reader.FieldCount > 0 ? reader.FieldCount - 1 : -1;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (totalOrdinal >= 0 && !reader.IsDBNull(totalOrdinal))
            {
                windowCount = reader.GetInt32(totalOrdinal);
            }

            var dto = MapSummary(reader, totalOrdinal);
            items.Add(dto);
        }

        var actualCount = await ExecuteGridCountAsync(
                connection,
                matchQuery!,
                parameters,
                today,
                cancellationToken)
            .ConfigureAwait(false);
        if (actualCount == 0)
        {
            actualCount = windowCount;
        }

        stopwatch.Stop();
        _telemetry.RecordFtsQuery(stopwatch.Elapsed);

        var cappedByPolicy = Math.Min(actualCount, _options.MaxCandidateResults);
        if (items.Count == 0 && cappedByPolicy == 0)
        {
            cappedByPolicy = windowCount;
        }

        var hasMore = effectiveOffset + items.Count < cappedByPolicy;
        var isTruncated = actualCount > _options.MaxCandidateResults;

        _telemetry.RecordFtsPagingMetrics(
            effectiveOffset,
            effectiveLimit,
            effectiveCandidateLimit,
            _options.MaxCandidateResults,
            items.Count,
            cappedByPolicy,
            actualCount,
            hasMore,
            isTruncated);

        return new FileGridSearchResult(items, cappedByPolicy, hasMore, isTruncated);
    }

    private static string BuildGridSql(
        IReadOnlyList<FileSortSpecDto> sort,
        FileGridQueryDto parameters,
        DateTimeOffset today,
        out IReadOnlyList<SqliteParameterDefinition> parameterDefinitions)
    {
        var sql = new StringBuilder();
        var defs = new List<SqliteParameterDefinition>();

        sql.AppendLine("WITH fts_matches AS (");
        sql.AppendLine("    SELECT");
        sql.AppendLine("        s.rowid AS id,");
        sql.AppendLine("        (1.0 / (1.0 + bm25(file_search))) AS fts_score");
        sql.AppendLine("    FROM file_search AS s");
        sql.AppendLine("    WHERE s MATCH $match");
        sql.AppendLine("    ORDER BY bm25(file_search)");
        sql.AppendLine("    LIMIT $candidateLimit");
        sql.AppendLine("), joined AS (");
        sql.AppendLine("    SELECT");
        sql.AppendLine("        m.id,");
        sql.AppendLine("        b.name,");
        sql.AppendLine("        b.title,");
        sql.AppendLine("        b.extension,");
        sql.AppendLine("        b.mime_type,");
        sql.AppendLine("        b.author,");
        sql.AppendLine("        b.size_bytes,");
        sql.AppendLine("        b.created_utc,");
        sql.AppendLine("        b.modified_utc,");
        sql.AppendLine("        b.version,");
        sql.AppendLine("        b.is_read_only,");
        sql.AppendLine("        b.fts_is_stale,");
        sql.AppendLine("        b.fts_last_indexed_utc,");
        sql.AppendLine("        b.fts_indexed_title,");
        sql.AppendLine("        b.fts_schema_version,");
        sql.AppendLine("        b.fts_indexed_hash,");
        sql.AppendLine("        b.validity_issued_at,");
        sql.AppendLine("        b.validity_valid_until,");
        sql.AppendLine("        b.validity_has_physical,");
        sql.AppendLine("        b.validity_has_electronic,");
        sql.AppendLine("        m.fts_score AS fts_score,");
        sql.AppendLine("        m.fts_score AS combined_score");
        sql.AppendLine("    FROM fts_matches AS m");
        sql.AppendLine("    JOIN v_file_search_base AS b ON b.id = m.id");
        sql.AppendLine("    WHERE 1 = 1");
        AppendGridFilters(sql, defs, parameters, today);
        sql.AppendLine(")");
        sql.AppendLine("SELECT");
        sql.AppendLine("    j.id,");
        sql.AppendLine("    j.name,");
        sql.AppendLine("    j.title,");
        sql.AppendLine("    j.extension,");
        sql.AppendLine("    j.mime_type,");
        sql.AppendLine("    j.author,");
        sql.AppendLine("    j.size_bytes,");
        sql.AppendLine("    j.created_utc,");
        sql.AppendLine("    j.modified_utc,");
        sql.AppendLine("    j.version,");
        sql.AppendLine("    j.is_read_only,");
        sql.AppendLine("    j.fts_is_stale,");
        sql.AppendLine("    j.fts_last_indexed_utc,");
        sql.AppendLine("    j.fts_indexed_title,");
        sql.AppendLine("    j.fts_schema_version,");
        sql.AppendLine("    j.fts_indexed_hash,");
        sql.AppendLine("    j.validity_issued_at,");
        sql.AppendLine("    j.validity_valid_until,");
        sql.AppendLine("    j.validity_has_physical,");
        sql.AppendLine("    j.validity_has_electronic,");
        sql.AppendLine("    j.fts_score,");
        sql.AppendLine("    j.combined_score,");
        sql.AppendLine("    COUNT(*) OVER() AS total_count");
        sql.AppendLine("FROM joined AS j");
        sql.Append("ORDER BY ").Append(BuildOrderClause(sort)).AppendLine();
        sql.AppendLine("LIMIT $limit OFFSET $offset;");

        parameterDefinitions = defs;
        return sql.ToString();
    }

    private static string BuildGridCountSql(
        FileGridQueryDto parameters,
        DateTimeOffset today,
        out IReadOnlyList<SqliteParameterDefinition> parameterDefinitions)
    {
        var sql = new StringBuilder();
        var defs = new List<SqliteParameterDefinition>();

        sql.AppendLine("WITH fts_matches AS (");
        sql.AppendLine("    SELECT");
        sql.AppendLine("        s.rowid AS id");
        sql.AppendLine("    FROM file_search AS s");
        sql.AppendLine("    WHERE s MATCH $match");
        sql.AppendLine("), joined AS (");
        sql.AppendLine("    SELECT");
        sql.AppendLine("        m.id");
        sql.AppendLine("    FROM fts_matches AS m");
        sql.AppendLine("    JOIN v_file_search_base AS b ON b.id = m.id");
        sql.AppendLine("    WHERE 1 = 1");
        AppendGridFilters(sql, defs, parameters, today);
        sql.AppendLine(")");
        sql.AppendLine("SELECT COUNT(*) FROM joined;");

        parameterDefinitions = defs;
        return sql.ToString();
    }

    private static string BuildOrderClause(IReadOnlyList<FileSortSpecDto> sort)
    {
        if (sort.Count == 0)
        {
            return "j.name COLLATE NOCASE ASC, j.id ASC";
        }

        var parts = new List<string>();

        foreach (var spec in sort)
        {
            if (string.IsNullOrWhiteSpace(spec.Field))
            {
                continue;
            }

            var direction = spec.Descending ? "DESC" : "ASC";
            switch (spec.Field.ToLowerInvariant())
            {
                case "score":
                    parts.Add($"j.combined_score {direction}");
                    break;
                case "name":
                    parts.Add($"j.name COLLATE NOCASE {direction}");
                    break;
                case "mime":
                    parts.Add($"j.mime_type COLLATE NOCASE {direction}");
                    break;
                case "extension":
                    parts.Add($"j.extension COLLATE NOCASE {direction}");
                    break;
                case "size":
                    parts.Add($"j.size_bytes {direction}");
                    break;
                case "createdutc":
                    parts.Add($"j.created_utc {direction}");
                    break;
                case "modifiedutc":
                    parts.Add($"j.modified_utc {direction}");
                    break;
                case "version":
                    parts.Add($"j.version {direction}");
                    break;
                case "author":
                    parts.Add($"j.author COLLATE NOCASE {direction}");
                    break;
                case "validuntil":
                    parts.Add($"j.validity_valid_until {direction}");
                    break;
            }
        }

        if (parts.Count == 0)
        {
            parts.Add("j.name COLLATE NOCASE ASC");
        }

        parts.Add("j.id ASC");
        return string.Join(", ", parts);
    }

    private static void AppendGridFilters(
        StringBuilder sql,
        List<SqliteParameterDefinition> parameters,
        FileGridQueryDto dto,
        DateTimeOffset today)
    {
        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var param = AddParameter(parameters, "name", dto.Name.Trim(), SqliteType.Text);
            sql.Append("        AND instr(lower(b.name), lower(").Append(param).AppendLine(")) > 0");
        }

        if (!string.IsNullOrWhiteSpace(dto.Extension))
        {
            var trimmed = dto.Extension.Trim();
            var sanitized = trimmed.TrimStart('.');
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                var normalized = sanitized.ToLowerInvariant();
                var param = AddParameter(parameters, "extension", normalized, SqliteType.Text);
                if (dto.ExtensionMatchMode == ExtensionMatchMode.Contains)
                {
                    sql.Append("        AND instr(lower(b.extension), ").Append(param).AppendLine(") > 0");
                }
                else
                {
                    sql.Append("        AND lower(b.extension) = ").Append(param).AppendLine();
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(dto.Mime))
        {
            var param = AddParameter(parameters, "mime", dto.Mime.Trim(), SqliteType.Text);
            sql.Append("        AND instr(lower(b.mime_type), lower(").Append(param).AppendLine(")) > 0");
        }

        if (!string.IsNullOrWhiteSpace(dto.Author))
        {
            var param = AddParameter(parameters, "author", dto.Author.Trim(), SqliteType.Text);
            sql.Append("        AND instr(lower(b.author), lower(").Append(param).AppendLine(")) > 0");
        }

        if (dto.IsReadOnly.HasValue)
        {
            var param = AddParameter(parameters, "readonly", dto.IsReadOnly.Value ? 1 : 0, SqliteType.Integer);
            sql.Append("        AND b.is_read_only = ").Append(param).AppendLine();
        }

        if (dto.IsIndexStale.HasValue)
        {
            var param = AddParameter(parameters, "stale", dto.IsIndexStale.Value ? 1 : 0, SqliteType.Integer);
            sql.Append("        AND b.fts_is_stale = ").Append(param).AppendLine();
        }

        if (dto.SizeMin.HasValue)
        {
            var param = AddParameter(parameters, "sizemin", dto.SizeMin.Value, SqliteType.Integer);
            sql.Append("        AND b.size_bytes >= ").Append(param).AppendLine();
        }

        if (dto.SizeMax.HasValue)
        {
            var param = AddParameter(parameters, "sizemax", dto.SizeMax.Value, SqliteType.Integer);
            sql.Append("        AND b.size_bytes <= ").Append(param).AppendLine();
        }

        if (dto.CreatedFromUtc.HasValue)
        {
            var param = AddParameter(parameters, "createdfrom", FormatDate(dto.CreatedFromUtc.Value), SqliteType.Text);
            sql.Append("        AND b.created_utc >= ").Append(param).AppendLine();
        }

        if (dto.CreatedToUtc.HasValue)
        {
            var param = AddParameter(parameters, "createdto", FormatDate(dto.CreatedToUtc.Value), SqliteType.Text);
            sql.Append("        AND b.created_utc <= ").Append(param).AppendLine();
        }

        if (dto.ModifiedFromUtc.HasValue)
        {
            var param = AddParameter(parameters, "modifiedfrom", FormatDate(dto.ModifiedFromUtc.Value), SqliteType.Text);
            sql.Append("        AND b.modified_utc >= ").Append(param).AppendLine();
        }

        if (dto.ModifiedToUtc.HasValue)
        {
            var param = AddParameter(parameters, "modifiedto", FormatDate(dto.ModifiedToUtc.Value), SqliteType.Text);
            sql.Append("        AND b.modified_utc <= ").Append(param).AppendLine();
        }

        if (dto.Version.HasValue)
        {
            var param = AddParameter(parameters, "version", dto.Version.Value, SqliteType.Integer);
            sql.Append("        AND b.version = ").Append(param).AppendLine();
        }

        if (dto.HasValidity.HasValue)
        {
            if (dto.HasValidity.Value)
            {
                sql.AppendLine("        AND b.validity_issued_at IS NOT NULL");
            }
            else
            {
                sql.AppendLine("        AND b.validity_issued_at IS NULL");
            }
        }

        if (dto.IsCurrentlyValid.HasValue)
        {
            var reference = AddParameter(parameters, "current", FormatDate(today), SqliteType.Text);
            if (dto.IsCurrentlyValid.Value)
            {
                sql.Append("        AND b.validity_issued_at IS NOT NULL AND b.validity_issued_at <= ").Append(reference)
                    .Append(" AND b.validity_valid_until >= ").Append(reference).AppendLine();
            }
            else
            {
                sql.Append("        AND (b.validity_issued_at IS NULL OR b.validity_issued_at > ").Append(reference)
                    .Append(" OR b.validity_valid_until < ").Append(reference).AppendLine(")");
            }
        }

        if (dto.ExpiringInDays.HasValue)
        {
            var start = AddParameter(parameters, "validstart", FormatDate(today), SqliteType.Text);
            var end = AddParameter(
                parameters,
                "validend",
                FormatDate(today.AddDays(dto.ExpiringInDays.Value)),
                SqliteType.Text);

            sql.Append("        AND b.validity_valid_until IS NOT NULL AND b.validity_valid_until >= ").Append(start)
                .Append(" AND b.validity_valid_until <= ").Append(end).AppendLine();
        }
    }

    private static string AddParameter(
        List<SqliteParameterDefinition> parameters,
        string name,
        object value,
        SqliteType type)
    {
        var parameterName = $"$fg_{name}_{parameters.Count}";
        parameters.Add(new SqliteParameterDefinition(parameterName, value, type));
        return parameterName;
    }

    private static async Task<int> ExecuteGridCountAsync(
        SqliteConnection connection,
        string matchQuery,
        FileGridQueryDto parameters,
        DateTimeOffset today,
        CancellationToken cancellationToken)
    {
        await using var countCommand = connection.CreateCommand();
        var countSql = BuildGridCountSql(parameters, today, out var countParameters);
        countCommand.CommandText = countSql;
        countCommand.Parameters.Add("$match", SqliteType.Text).Value = matchQuery;

        foreach (var definition in countParameters)
        {
            var parameter = countCommand.Parameters.Add(definition.Name, definition.Type ?? SqliteType.Text);
            parameter.Value = definition.Value ?? DBNull.Value;
        }

        var scalar = await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (scalar is null || scalar is DBNull)
        {
            return 0;
        }

        return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    private static FileSummaryDto MapSummary(SqliteDataReader reader, int totalOrdinal)
    {
        _ = totalOrdinal;
        var idBytes = (byte[])reader.GetValue(0);
        var id = new Guid(idBytes);

        FileValidityDto? validity = null;
        if (!reader.IsDBNull(16) && !reader.IsDBNull(17))
        {
            var issued = ParseDateTime(reader, 16);
            var validUntil = ParseDateTime(reader, 17);
            var hasPhysical = !reader.IsDBNull(18) && reader.GetInt32(18) != 0;
            var hasElectronic = !reader.IsDBNull(19) && reader.GetInt32(19) != 0;
            validity = new FileValidityDto(issued, validUntil, hasPhysical, hasElectronic);
        }

        var summary = new FileSummaryDto
        {
            Id = id,
            Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            Title = ReadNullableString(reader, 2),
            Extension = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            Mime = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            Author = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            Size = reader.IsDBNull(6) ? 0L : reader.GetInt64(6),
            CreatedUtc = ParseDateTime(reader, 7),
            LastModifiedUtc = ParseDateTime(reader, 8),
            Version = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
            IsReadOnly = ReadBoolean(reader, 10),
            IsIndexStale = ReadBoolean(reader, 11),
            LastIndexedUtc = ParseNullableDateTime(reader, 12),
            IndexedTitle = ReadNullableString(reader, 13),
            IndexSchemaVersion = reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
            IndexedContentHash = ReadNullableString(reader, 15),
            Validity = validity,
            Score = reader.IsDBNull(21) ? null : reader.GetDouble(21),
        };

        return summary;
    }

    private static DateTimeOffset ParseDateTime(SqliteDataReader reader, int ordinal)
    {
        var text = reader.GetString(ordinal);
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static DateTimeOffset? ParseNullableDateTime(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static bool ReadBoolean(SqliteDataReader reader, int ordinal)
        => !reader.IsDBNull(ordinal) && reader.GetInt32(ordinal) != 0;

    private static string FormatDate(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<(Guid Id, double Score)>> SearchWithScoresAsync(
        SearchQueryPlan plan,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (take <= 0)
        {
            return Array.Empty<(Guid, double)>();
        }

        if (skip < 0)
        {
            skip = 0;
        }

        if (!_options.IsFulltextAvailable)
        {
            return Array.Empty<(Guid, double)>();
        }

        if (string.IsNullOrWhiteSpace(plan.MatchExpression))
        {
            return Array.Empty<(Guid, double)>();
        }

        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var (bm25Expression, rankExpression) = BuildRankExpressions(plan.ScorePlan);
        command.CommandText = BuildScoreQuery(plan, bm25Expression, rankExpression);
        command.Parameters.Add("$query", SqliteType.Text).Value = plan.MatchExpression;
        command.Parameters.Add("$take", SqliteType.Integer).Value = take;
        command.Parameters.Add("$skip", SqliteType.Integer).Value = skip;
        var normalizedRaw = NormalizeForMatch(plan.RawQueryText, plan.MatchExpression);
        command.Parameters.Add("$raw", SqliteType.Text).Value = normalizedRaw;
        ApplyPlanParameters(command, plan);

        var results = new List<(Guid, double)>();
        var stopwatch = Stopwatch.StartNew();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var hasCustomSimilarity = !string.IsNullOrWhiteSpace(plan.ScorePlan.CustomSimilaritySql);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var idBytes = (byte[])reader[0];
            var id = new Guid(idBytes);
            var bm25Score = reader.IsDBNull(1) ? double.PositiveInfinity : reader.GetDouble(1);
            var rawScore = reader.IsDBNull(2) ? bm25Score : reader.GetDouble(2);
            double? customSimilarity = null;
            DateTimeOffset? lastModified = null;
            var offset = 3;
            if (hasCustomSimilarity)
            {
                customSimilarity = reader.IsDBNull(offset) ? null : reader.GetDouble(offset);
                offset++;
            }

            if (!reader.IsDBNull(offset))
            {
                var modifiedUtcRaw = reader.GetString(offset);
                lastModified = DateTimeOffset.Parse(modifiedUtcRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }

            var score = ComputeNormalizedScore(rawScore, plan.ScorePlan);
            if (plan.ScorePlan.CustomSimilarityDelegate is not null)
            {
                var adjusted = plan.ScorePlan.CustomSimilarityDelegate(bm25Score, customSimilarity, lastModified);
                score = Math.Clamp(adjusted, 0d, 1d);
            }

            results.Add((id, score));
        }

        stopwatch.Stop();
        _telemetry.RecordFtsQuery(stopwatch.Elapsed);

        return results;
    }

    public async Task<int> CountAsync(SearchQueryPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!_options.IsFulltextAvailable)
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(plan.MatchExpression))
        {
            return 0;
        }

        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var builder = new StringBuilder();
        builder.Append("SELECT COUNT(*) FROM ").Append(SearchTableName).Append(' ').Append(SearchTableAlias).Append(' ');
        builder.Append("JOIN file_search_map m ON s.rowid = m.rowid ");
        builder.Append("JOIN files f ON f.id = m.file_id ");
        builder.Append("WHERE ").Append(SearchTableName).Append(" MATCH $query ");
        AppendWhereClauses(builder, plan);
        builder.Append(';');
        command.CommandText = builder.ToString();
        command.Parameters.Add("$query", SqliteType.Text).Value = plan.MatchExpression;
        ApplyPlanParameters(command, plan);

        var stopwatch = Stopwatch.StartNew();
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        _telemetry.RecordFtsQuery(stopwatch.Elapsed);
        return result is long value ? (int)value : 0;
    }

    public async Task<FtsSearchResult> SearchAsync(
        SearchQueryPlan plan,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (take <= 0)
        {
            return FtsSearchResult.Empty;
        }

        if (!_options.IsFulltextAvailable)
        {
            return FtsSearchResult.Empty;
        }

        if (string.IsNullOrWhiteSpace(plan.MatchExpression))
        {
            return FtsSearchResult.Empty;
        }

        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = lease.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var (bm25Expression, rankExpression) = BuildRankExpressions(plan.ScorePlan);
        var hasCustomSimilarity = !string.IsNullOrWhiteSpace(plan.ScorePlan.CustomSimilaritySql);
        command.CommandText = BuildHitQuery(plan, bm25Expression, rankExpression, hasCustomSimilarity);
        command.Parameters.Add("$query", SqliteType.Text).Value = plan.MatchExpression;
        command.Parameters.Add("$take", SqliteType.Integer).Value = take;
        var normalizedRaw = NormalizeForMatch(plan.RawQueryText, plan.MatchExpression);
        command.Parameters.Add("$raw", SqliteType.Text).Value = normalizedRaw;
        ApplyPlanParameters(command, plan);

        var hits = new List<SearchHit>(take);
        double? topNormalizedScore = null;
        var stopwatch = Stopwatch.StartNew();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var hit = MapHit(reader, plan.ScorePlan, hasCustomSimilarity, out var normalizedScore);
            topNormalizedScore ??= normalizedScore;
            hits.Add(hit);
        }

        stopwatch.Stop();
        _telemetry.RecordFtsQuery(stopwatch.Elapsed);

        return hits.Count == 0
            ? FtsSearchResult.Empty
            : new FtsSearchResult(hits, hits.Count, topNormalizedScore);
    }

    private SearchHit MapHit(
        SqliteDataReader reader,
        SearchScorePlan scorePlan,
        bool hasCustomSimilarity,
        out double normalizedScore)
    {
        var fileIdBytes = (byte[])reader[1];
        var id = new Guid(fileIdBytes);
        var title = reader.GetString(2);
        var mime = reader.GetString(3);
        var author = reader.GetString(4);
        var metadataText = reader.GetString(5);
        var metadataJson = reader.GetString(6);

        var snippets = new Dictionary<int, string>(5)
        {
            [0] = ReadValue(reader, 7),
            [1] = ReadValue(reader, 8),
            [2] = ReadValue(reader, 9),
            [3] = ReadValue(reader, 10),
            [4] = ReadValue(reader, 11),
        };

        var offsetsRaw = ReadValue(reader, 12);
        var offsetsByColumn = ParseOffsets(offsetsRaw);
        var bm25Score = reader.IsDBNull(13) ? double.PositiveInfinity : reader.GetDouble(13);
        var rawScore = reader.IsDBNull(14) ? bm25Score : reader.GetDouble(14);
        double? customSimilarity = null;
        var modifiedIndex = 15;
        if (hasCustomSimilarity)
        {
            customSimilarity = reader.IsDBNull(modifiedIndex) ? null : reader.GetDouble(modifiedIndex);
            modifiedIndex++;
        }

        var modifiedUtcRaw = reader.GetString(modifiedIndex);
        var modifiedUtc = DateTimeOffset.Parse(modifiedUtcRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        var columnValues = new Dictionary<int, string>
        {
            [0] = title,
            [1] = mime,
            [2] = author,
            [3] = metadataText,
            [4] = metadataJson,
        };

        var selectedColumn = SelectSnippetColumn(snippets);
        var primaryField = ColumnNameMap.TryGetValue(selectedColumn, out var fieldName)
            ? fieldName
            : ColumnNameMap[3];

        var snippetText = snippets.TryGetValue(selectedColumn, out var rawSnippet)
            ? NormaliseSnippet(rawSnippet)
            : string.Empty;

        if (string.IsNullOrWhiteSpace(snippetText))
        {
            var fallback = columnValues.TryGetValue(selectedColumn, out var value)
                ? value
                : metadataText;
            snippetText = BuildFallbackSnippet(fallback);
        }

        var highlights = BuildHighlights(selectedColumn, snippetText, columnValues, offsetsByColumn);
        var fields = BuildFields(title, mime, author, metadataText, metadataJson);
        fields["last_modified_utc"] = modifiedUtc.ToString("O", CultureInfo.InvariantCulture);
        normalizedScore = ComputeNormalizedScore(rawScore, scorePlan);
        if (scorePlan.CustomSimilarityDelegate is not null)
        {
            var adjusted = scorePlan.CustomSimilarityDelegate(bm25Score, customSimilarity, modifiedUtc);
            normalizedScore = Math.Clamp(adjusted, 0d, 1d);
        }

        var sort = new SearchHitSortValues(modifiedUtc, normalizedScore, rawScore, customSimilarity);

        return new SearchHit(
            id,
            rawScore,
            "FTS",
            primaryField,
            snippetText,
            highlights,
            fields,
            sort);
    }

    private static (string Bm25Expression, string RankExpression) BuildRankExpressions(SearchScorePlan scorePlan)
    {
        var bm25 = string.Format(
            CultureInfo.InvariantCulture,
            "bm25(" + SearchTableName + ", {0}, {1}, {2}, {3}, {4})",
            scorePlan.TitleWeight,
            scorePlan.MimeWeight,
            scorePlan.AuthorWeight,
            scorePlan.MetadataTextWeight,
            scorePlan.MetadataWeight);

        string rank;
        if (!string.IsNullOrWhiteSpace(scorePlan.CustomRankExpression))
        {
            rank = ExpandCustomExpression(scorePlan.CustomRankExpression, bm25);
        }
        else if (scorePlan.UseTfIdfAlternative)
        {
            rank = string.Format(
                CultureInfo.InvariantCulture,
                "(1.0 / ({0} + {1}))",
                scorePlan.TfIdfDampingFactor,
                bm25);
        }
        else
        {
            rank = bm25;
        }

        var recencyExpression = BuildRecencyExpression(scorePlan);
        if (!string.IsNullOrWhiteSpace(recencyExpression))
        {
            rank = string.Format(
                CultureInfo.InvariantCulture,
                scorePlan.HigherScoreIsBetter ? "({0} / {1})" : "({0} * {1})",
                rank,
                recencyExpression);
        }

        if (Math.Abs(scorePlan.ScoreMultiplier - 1d) > double.Epsilon)
        {
            rank = string.Format(CultureInfo.InvariantCulture, "({0} * {1})", rank, scorePlan.ScoreMultiplier);
        }

        return (bm25, rank);
    }

    private static string BuildRecencyExpression(SearchScorePlan scorePlan)
    {
        if (scorePlan.RecencyHalfLifeDays <= 0d)
        {
            return string.Empty;
        }

        var halfLife = scorePlan.RecencyHalfLifeDays.ToString(CultureInfo.InvariantCulture);
        return string.Format(
            CultureInfo.InvariantCulture,
            "(1.0 + (MAX(0.0, julianday('now') - julianday(COALESCE(f.modified_utc, f.created_utc, '1970-01-01T00:00:00Z'))) / {0}))",
            halfLife);
    }

    private string BuildScoreQuery(SearchQueryPlan plan, string bm25Expression, string rankExpression)
    {
        var builder = new StringBuilder();
        builder.Append("SELECT m.file_id, ");
        builder.Append(bm25Expression).Append(" AS bm25_score, ");
        builder.Append(rankExpression).Append(" AS score");

        var customSimilarity = ExpandCustomExpression(plan.ScorePlan.CustomSimilaritySql, bm25Expression);
        if (!string.IsNullOrWhiteSpace(customSimilarity))
        {
            builder.Append(", ").Append(customSimilarity).Append(" AS custom_similarity");
        }

        builder.Append(", f.modified_utc ");
        builder.Append("FROM ").Append(SearchTableName).Append(' ').Append(SearchTableAlias).Append(' ');
        builder.Append("JOIN file_search_map m ON s.rowid = m.rowid ");
        builder.Append("JOIN files f ON f.id = m.file_id ");
        builder.Append("WHERE ").Append(SearchTableName).Append(" MATCH $query ");
        AppendWhereClauses(builder, plan);
        builder.Append("ORDER BY score ");
        builder.Append(plan.ScorePlan.HigherScoreIsBetter ? "DESC" : "ASC");
        const string TitleSortExpression = "COALESCE(f.title, '')";
        builder.Append(", f.modified_utc DESC, CASE WHEN lower(");
        builder.Append(TitleSortExpression);
        builder.Append(") = lower($raw) THEN 0 ELSE 1 END, ");
        builder.Append(TitleSortExpression);
        builder.Append(" COLLATE NOCASE ");
        builder.Append("LIMIT $take OFFSET $skip;");
        return builder.ToString();
    }

    private string BuildHitQuery(
        SearchQueryPlan plan,
        string bm25Expression,
        string rankExpression,
        bool hasCustomSimilarity)
    {
        var builder = new StringBuilder();
        builder.Append(
            "SELECT s.rowid, " +
            "       m.file_id, " +
            "       COALESCE(f.title, s.title, '') AS title, " +
            "       COALESCE(f.mime, s.mime, '') AS mime, " +
            "       COALESCE(f.author, s.author, '') AS author, " +
            "       COALESCE(s.metadata_text, '') AS metadata_text, " +
            "       COALESCE(s.metadata, '') AS metadata_json, " +
            "       snippet(" + SearchTableName + ", 0, '', '', '…', 32) AS snippet_title, " +
            "       snippet(" + SearchTableName + ", 1, '', '', '…', 24) AS snippet_mime, " +
            "       snippet(" + SearchTableName + ", 2, '', '', '…', 24) AS snippet_author, " +
            "       snippet(" + SearchTableName + ", 3, '', '', '…', 32) AS snippet_metadata_text, " +
            "       snippet(" + SearchTableName + ", 4, '', '', '…', 32) AS snippet_metadata_json, " +
            "       offsets(" + SearchTableName + ") AS offsets, ");
        builder.Append(bm25Expression).Append(" AS bm25_score, ");
        builder.Append(rankExpression).Append(" AS score");

        if (hasCustomSimilarity)
        {
            var custom = ExpandCustomExpression(plan.ScorePlan.CustomSimilaritySql, bm25Expression);
            builder.Append(", ").Append(custom).Append(" AS custom_similarity");
        }

        builder.Append(", f.modified_utc ");
        builder.Append("FROM ").Append(SearchTableName).Append(' ').Append(SearchTableAlias).Append(' ');
        builder.Append("JOIN file_search_map m ON s.rowid = m.rowid ");
        builder.Append("JOIN files f ON f.id = m.file_id ");
        builder.Append("WHERE ").Append(SearchTableName).Append(" MATCH $query ");
        AppendWhereClauses(builder, plan);
        builder.Append("ORDER BY score ");
        builder.Append(plan.ScorePlan.HigherScoreIsBetter ? "DESC" : "ASC");
        builder.Append(", f.modified_utc DESC, CASE WHEN lower(title) = lower($raw) THEN 0 ELSE 1 END, title COLLATE NOCASE ");
        builder.Append("LIMIT $take;");
        return builder.ToString();
    }

    private static string ExpandCustomExpression(string? expression, string bm25Expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return string.Empty;
        }

        return expression.Replace("bm25_score", bm25Expression, StringComparison.OrdinalIgnoreCase);
    }

    private string NormalizeForMatch(string? raw, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(raw) ? fallback : raw!;
        return TextNormalization.NormalizeText(source, _analyzerFactory);
    }

    private static void AppendWhereClauses(StringBuilder builder, SearchQueryPlan plan)
    {
        foreach (var clause in plan.WhereClauses)
        {
            if (!string.IsNullOrWhiteSpace(clause))
            {
                builder.Append("AND ").Append(clause).Append(' ');
            }
        }
    }

    private static void ApplyPlanParameters(SqliteCommand command, SearchQueryPlan plan)
    {
        foreach (var parameter in plan.Parameters)
        {
            SqliteParameter sqliteParameter;
            if (parameter.Type.HasValue)
            {
                sqliteParameter = command.Parameters.Add(parameter.Name, parameter.Type.Value);
                sqliteParameter.Value = parameter.Value ?? DBNull.Value;
            }
            else
            {
                sqliteParameter = command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            }
        }
    }

    private static double ComputeNormalizedScore(double rawScore, SearchScorePlan scorePlan)
    {
        var normalized = NormalizeScore(rawScore);
        if (scorePlan.HigherScoreIsBetter)
        {
            return 1d - normalized;
        }

        return normalized;
    }

    private static List<HighlightSpan> BuildHighlights(
        int columnIndex,
        string snippet,
        IReadOnlyDictionary<int, string> columnValues,
        IReadOnlyDictionary<int, IReadOnlyList<OffsetInfo>> offsetsByColumn)
    {
        if (!offsetsByColumn.TryGetValue(columnIndex, out var offsets) || offsets.Count == 0)
        {
            return new List<HighlightSpan>();
        }

        if (!columnValues.TryGetValue(columnIndex, out var columnText) || string.IsNullOrEmpty(columnText))
        {
            return new List<HighlightSpan>();
        }

        if (!ColumnNameMap.TryGetValue(columnIndex, out var field))
        {
            field = ColumnNameMap[3];
        }

        if (string.IsNullOrEmpty(snippet))
        {
            snippet = columnText;
        }

        var map = BuildSnippetIndexMap(columnText, snippet);
        if (map.Count == 0)
        {
            return new List<HighlightSpan>();
        }

        var columnBytes = Utf8.GetBytes(columnText);
        var spans = new List<HighlightSpan>();
        var seen = new HashSet<(int Start, int Length)>();

        foreach (var offset in offsets)
        {
            var charStart = ByteOffsetToCharIndex(columnBytes, offset.ByteStart);
            var charEnd = ByteOffsetToCharIndex(columnBytes, offset.ByteEnd);
            if (charEnd < charStart)
            {
                continue;
            }

            var charLength = Math.Max(1, charEnd - charStart);
            if (charStart >= columnText.Length)
            {
                continue;
            }

            if (charStart + charLength > columnText.Length)
            {
                charLength = columnText.Length - charStart;
            }

            var snippetPositions = new List<int>(charLength);
            for (var index = 0; index < charLength; index++)
            {
                if (map.TryGetValue(charStart + index, out var snippetIndex))
                {
                    snippetPositions.Add(snippetIndex);
                }
            }

            if (snippetPositions.Count == 0)
            {
                continue;
            }

            snippetPositions.Sort();
            var spanStart = snippetPositions[0];
            var spanEnd = snippetPositions[^1];
            var length = spanEnd - spanStart + 1;
            if (length <= 0)
            {
                continue;
            }

            var key = (spanStart, length);
            if (!seen.Add(key))
            {
                continue;
            }

            var term = SafeSubstring(columnText, charStart, charLength);
            spans.Add(new HighlightSpan(field, spanStart, length, term));
        }

        return spans;
    }

    private static Dictionary<string, string?> BuildFields(
        string title,
        string mime,
        string author,
        string metadataText,
        string metadataJson)
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = string.IsNullOrWhiteSpace(title) ? null : title,
            ["mime"] = string.IsNullOrWhiteSpace(mime) ? null : mime,
            ["author"] = string.IsNullOrWhiteSpace(author) ? null : author,
            ["metadata_text"] = string.IsNullOrWhiteSpace(metadataText) ? null : metadataText,
            ["metadata"] = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson,
        };
    }

    private static Dictionary<int, int> BuildSnippetIndexMap(string columnText, string snippet)
    {
        var map = new Dictionary<int, int>();
        if (string.IsNullOrEmpty(columnText) || string.IsNullOrEmpty(snippet))
        {
            return map;
        }

        var searchStart = 0;
        var snippetIndex = 0;
        while (snippetIndex < snippet.Length)
        {
            if (snippet[snippetIndex] == Ellipsis)
            {
                snippetIndex++;
                continue;
            }

            var segmentStart = snippetIndex;
            while (snippetIndex < snippet.Length && snippet[snippetIndex] != Ellipsis)
            {
                snippetIndex++;
            }

            var segmentLength = snippetIndex - segmentStart;
            if (segmentLength == 0)
            {
                continue;
            }

            var segment = snippet.Substring(segmentStart, segmentLength);
            var found = columnText.IndexOf(segment, searchStart, StringComparison.Ordinal);
            if (found < 0)
            {
                found = columnText.IndexOf(segment, StringComparison.Ordinal);
                if (found < 0)
                {
                    continue;
                }
            }

            for (var i = 0; i < segmentLength; i++)
            {
                map[found + i] = segmentStart + i;
            }

            searchStart = found + segmentLength;
        }

        return map;
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<OffsetInfo>> ParseOffsets(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new Dictionary<int, IReadOnlyList<OffsetInfo>>();
        }

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4)
        {
            return new Dictionary<int, IReadOnlyList<OffsetInfo>>();
        }

        var result = new Dictionary<int, IReadOnlyList<OffsetInfo>>();
        var temp = new Dictionary<int, List<OffsetInfo>>();
        for (var index = 0; index + 3 < parts.Length; index += 4)
        {
            if (!int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var column))
            {
                continue;
            }

            if (!int.TryParse(parts[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var termIndex))
            {
                continue;
            }

            if (!int.TryParse(parts[index + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteStart))
            {
                continue;
            }

            if (!int.TryParse(parts[index + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteEnd))
            {
                continue;
            }

            if (!temp.TryGetValue(column, out var list))
            {
                list = new List<OffsetInfo>();
                temp[column] = list;
            }

            list.Add(new OffsetInfo(column, termIndex, byteStart, byteEnd));
        }

        foreach (var (column, list) in temp)
        {
            result[column] = list;
        }

        return result;
    }

    private static int SelectSnippetColumn(IReadOnlyDictionary<int, string> snippets)
    {
        foreach (var column in SnippetPriority)
        {
            if (snippets.TryGetValue(column, out var snippet) && !string.IsNullOrWhiteSpace(snippet))
            {
                return column;
            }
        }

        return 3; // metadata_text fallback
    }

    private static string BuildFallbackSnippet(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length <= 240)
        {
            return normalized;
        }

        return normalized[..240] + Ellipsis;
    }

    private static string NormaliseSnippet(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return string.Empty;
        }

        var normalized = snippet.ReplaceLineEndings(" ");
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string SafeSubstring(string text, int start, int length)
    {
        if (start < 0)
        {
            start = 0;
        }

        if (start >= text.Length)
        {
            return string.Empty;
        }

        if (length <= 0)
        {
            return string.Empty;
        }

        if (start + length > text.Length)
        {
            length = text.Length - start;
        }

        return text.Substring(start, length);
    }

    private static int ByteOffsetToCharIndex(byte[] bytes, int byteOffset)
    {
        if (byteOffset <= 0)
        {
            return 0;
        }

        if (byteOffset >= bytes.Length)
        {
            return Utf8.GetCharCount(bytes);
        }

        return Utf8.GetCharCount(bytes, 0, byteOffset);
    }

    private static string ReadValue(SqliteDataReader source, int ordinal)
    {
        if (ordinal < 0 || source.IsDBNull(ordinal))
        {
            return string.Empty;
        }

        return source.GetString(ordinal);
    }

    private static double NormalizeScore(double rawScore)
    {
        if (double.IsNaN(rawScore) || double.IsInfinity(rawScore))
        {
            return 0d;
        }

        var clamped = Math.Max(0d, rawScore);
        return 1d / (1d + clamped);
    }

    private sealed record OffsetInfo(int Column, int TermIndex, int ByteStart, int ByteEnd);

}


/// <summary>
/// Represents the result of an FTS search operation including diagnostic metadata.
/// </summary>
internal sealed record FtsSearchResult(
    IReadOnlyList<SearchHit> Hits,
    int HitCount,
    double? TopNormalizedScore)
{
    public static readonly FtsSearchResult Empty = new(Array.Empty<SearchHit>(), 0, null);
}
