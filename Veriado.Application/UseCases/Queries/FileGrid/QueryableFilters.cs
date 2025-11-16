using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Veriado.Contracts.Files;

namespace Veriado.Appl.UseCases.Queries.FileGrid;

/// <summary>
/// Provides reusable filtering and ordering logic for file grid queries.
/// </summary>
internal static class QueryableFilters
{
    private const string EscapeChar = "\\";
    private static readonly Expression<Func<FileEntity, string>> NameSortSelector =
        file => EF.Property<string>(file, nameof(FileEntity.Name));

    private static readonly Expression<Func<FileEntity, string>> MimeSortSelector =
        file => EF.Property<string>(file, nameof(FileEntity.Mime));

    private static readonly Expression<Func<FileEntity, string>> ExtensionSortSelector =
        file => EF.Property<string>(file, nameof(FileEntity.Extension));

    private static readonly Expression<Func<FileEntity, long?>> ContentSizeSelector =
        file => file.Content != null ? (long?)file.Content.Size.Value : null;

    /// <summary>
    /// Applies the filters defined in <see cref="FileGridQueryDto"/> to the provided query.
    /// </summary>
    public static IQueryable<FileEntity> ApplyFilters(
        IQueryable<FileEntity> query,
        FileGridQueryDto dto,
        DateTimeOffset referenceTime)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(dto);

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var pattern = $"%{EscapeLike(dto.Name)}%";
            query = query.Where(file =>
                EF.Functions.Like(EF.Property<string>(file, nameof(FileEntity.Name)), pattern, EscapeChar));
        }

        if (!string.IsNullOrWhiteSpace(dto.Extension))
        {
            var pattern = $"%{EscapeLike(dto.Extension)}%";
            query = query.Where(file =>
                EF.Functions.Like(EF.Property<string>(file, nameof(FileEntity.Extension)), pattern, EscapeChar));
        }

        if (!string.IsNullOrWhiteSpace(dto.Mime))
        {
            var pattern = $"%{EscapeLike(dto.Mime)}%";
            query = query.Where(file =>
                EF.Functions.Like(EF.Property<string>(file, nameof(FileEntity.Mime)), pattern, EscapeChar));
        }

        if (!string.IsNullOrWhiteSpace(dto.Author))
        {
            var pattern = $"%{EscapeLike(dto.Author)}%";
            query = query.Where(file => EF.Functions.Like(file.Author, pattern, EscapeChar));
        }

        if (dto.IsReadOnly.HasValue)
        {
            query = query.Where(file => file.IsReadOnly == dto.IsReadOnly.Value);
        }

        if (dto.IsIndexStale.HasValue)
        {
            query = query.Where(file =>
                EF.Property<bool>(file, "fts_is_stale") == dto.IsIndexStale.Value);
        }

        var referenceInstant = referenceTime.ToUniversalTime();

        if (dto.ValidityFilterMode != ValidityFilterMode.None)
        {
            query = ApplyAdvancedValidityFilter(query, dto, referenceInstant);
        }
        else
        {
            if (dto.HasValidity.HasValue)
            {
                if (dto.HasValidity.Value)
                {
                    query = query.Where(file => file.Validity != null);
                }
                else
                {
                    query = query.Where(file => file.Validity == null);
                }
            }

            if (dto.IsCurrentlyValid.HasValue)
            {
                if (dto.IsCurrentlyValid.Value)
                {
                    query = query.Where(file => file.Validity != null
                        && EF.Property<DateTimeOffset?>(file, "Validity_IssuedAt") <= referenceInstant
                        && EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil") >= referenceInstant);
                }
                else
                {
                    query = query.Where(file => file.Validity == null
                        || EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil") < referenceInstant
                        || EF.Property<DateTimeOffset?>(file, "Validity_IssuedAt") > referenceInstant);
                }
            }

            if (dto.ExpiringInDays.HasValue)
            {
                var days = dto.ExpiringInDays.Value;
                var horizon = referenceInstant.AddDays(days);
                query = query.Where(file => file.Validity != null
                    && EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil") >= referenceInstant
                    && EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil") <= horizon);
            }
        }

        if (dto.SizeMin.HasValue)
        {
            query = query.Where(file => file.Content != null
                && file.Content.Size.Value >= dto.SizeMin.Value);
        }

        if (dto.SizeMax.HasValue)
        {
            query = query.Where(file => file.Content != null
                && file.Content.Size.Value <= dto.SizeMax.Value);
        }

        if (dto.CreatedFromUtc.HasValue)
        {
            var fromIso = dto.CreatedFromUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            query = query.Where(file => string.Compare(
                EF.Property<string>(file, nameof(FileEntity.CreatedUtc)),
                fromIso,
                StringComparison.Ordinal) >= 0);
        }

        if (dto.CreatedToUtc.HasValue)
        {
            var toIso = dto.CreatedToUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            query = query.Where(file => string.Compare(
                EF.Property<string>(file, nameof(FileEntity.CreatedUtc)),
                toIso,
                StringComparison.Ordinal) <= 0);
        }

        if (dto.ModifiedFromUtc.HasValue)
        {
            var fromIso = dto.ModifiedFromUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            query = query.Where(file => string.Compare(
                EF.Property<string>(file, nameof(FileEntity.LastModifiedUtc)),
                fromIso,
                StringComparison.Ordinal) >= 0);
        }

        if (dto.ModifiedToUtc.HasValue)
        {
            var toIso = dto.ModifiedToUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            query = query.Where(file => string.Compare(
                EF.Property<string>(file, nameof(FileEntity.LastModifiedUtc)),
                toIso,
                StringComparison.Ordinal) <= 0);
        }

        if (dto.Version.HasValue)
        {
            query = query.Where(file => file.ContentRevision == dto.Version.Value);
        }

        return query;
    }

    private static IQueryable<FileEntity> ApplyAdvancedValidityFilter(
        IQueryable<FileEntity> query,
        FileGridQueryDto dto,
        DateTimeOffset referenceInstant)
    {
        switch (dto.ValidityFilterMode)
        {
            case ValidityFilterMode.HasValidity:
                return query.Where(file => file.Validity != null);
            case ValidityFilterMode.NoValidity:
                return query.Where(file => file.Validity == null);
            case ValidityFilterMode.CurrentlyValid:
                return query.Where(file => file.Validity != null
                    && EF.Property<DateTimeOffset?>(file, "Validity_IssuedAt") <= referenceInstant
                    && EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil") >= referenceInstant);
            case ValidityFilterMode.Expired:
                return query.Where(file => file.Validity != null
                    && EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil") < referenceInstant);
            case ValidityFilterMode.ExpiringWithin:
                if (!dto.ValidityFilterValue.HasValue)
                {
                    return query;
                }

                {
                    var horizon = AddRelative(referenceInstant, dto.ValidityFilterUnit, dto.ValidityFilterValue.Value);
                    return query.Where(file => file.Validity != null
                        && EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil") >= referenceInstant
                        && EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil") <= horizon);
                }

            case ValidityFilterMode.ExpiringRange:
                if (!dto.ValidityFilterRangeFrom.HasValue || !dto.ValidityFilterRangeTo.HasValue)
                {
                    return query;
                }

                {
                    var start = AddRelative(referenceInstant, dto.ValidityFilterUnit, dto.ValidityFilterRangeFrom.Value);
                    var end = AddRelative(referenceInstant, dto.ValidityFilterUnit, dto.ValidityFilterRangeTo.Value);
                    if (end < start)
                    {
                        (start, end) = (end, start);
                    }

                    return query.Where(file => file.Validity != null
                        && EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil") >= start
                        && EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil") <= end);
                }
            default:
                return query;
        }
    }

    private static DateTimeOffset AddRelative(DateTimeOffset reference, ValidityRelativeUnit unit, int amount)
    {
        return unit switch
        {
            ValidityRelativeUnit.Days => reference.AddDays(amount),
            ValidityRelativeUnit.Weeks => reference.AddDays(amount * 7),
            ValidityRelativeUnit.Months => reference.AddMonths(amount),
            ValidityRelativeUnit.Years => reference.AddYears(amount),
            _ => reference.AddDays(amount),
        };
    }

    /// <summary>
    /// Applies ordering instructions to the query. Score-based ordering is handled separately.
    /// </summary>
    public static IQueryable<FileEntity> ApplyOrdering(
        IQueryable<FileEntity> query,
        IReadOnlyList<FileSortSpecDto> sort)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (sort is null || sort.Count == 0)
        {
            return query.OrderBy(NameSortSelector)
                .ThenBy(file => file.Id);
        }

        var orderedQuery = query;
        var ordered = false;

        foreach (var spec in sort)
        {
            if (spec.Field.Equals("score", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            orderedQuery = spec.Field.ToLowerInvariant() switch
            {
                "name" => ApplyOrder(orderedQuery, NameSortSelector, spec.Descending, ref ordered),
                "mime" => ApplyOrder(orderedQuery, MimeSortSelector, spec.Descending, ref ordered),
                "extension" => ApplyOrder(orderedQuery, ExtensionSortSelector, spec.Descending, ref ordered),
                "size" => ApplyOrder(orderedQuery, ContentSizeSelector, spec.Descending, ref ordered),
                "createdutc" => ApplyOrder(orderedQuery, file => EF.Property<string>(file, nameof(FileEntity.CreatedUtc)), spec.Descending, ref ordered),
                "modifiedutc" => ApplyOrder(orderedQuery, file => EF.Property<string>(file, nameof(FileEntity.LastModifiedUtc)), spec.Descending, ref ordered),
                "version" => ApplyOrder(orderedQuery, file => file.ContentRevision, spec.Descending, ref ordered),
                "author" => ApplyOrder(orderedQuery, file => file.Author, spec.Descending, ref ordered),
                "validuntil" => ApplyOrder(
                    orderedQuery,
                    file => EF.Property<DateTimeOffset?>(file, "Validity_ValidUntil"),
                    spec.Descending,
                    ref ordered),
                _ => orderedQuery,
            };
        }

        if (!ordered)
        {
            orderedQuery = orderedQuery.OrderBy(NameSortSelector);
        }

        return ((IOrderedQueryable<FileEntity>)orderedQuery).ThenBy(file => file.Id);
    }

    private static IQueryable<FileEntity> ApplyOrder<T>(
        IQueryable<FileEntity> query,
        Expression<Func<FileEntity, T>> selector,
        bool descending,
        ref bool ordered)
    {
        if (ordered)
        {
            var orderedQuery = (IOrderedQueryable<FileEntity>)query;
            return descending ? orderedQuery.ThenByDescending(selector) : orderedQuery.ThenBy(selector);
        }

        ordered = true;
        return descending ? query.OrderByDescending(selector) : query.OrderBy(selector);
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }
}
