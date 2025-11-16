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
            query = query.Where(file => EF.Functions.Like(file.Name.Value, pattern, EscapeChar));
        }

        if (!string.IsNullOrWhiteSpace(dto.Extension))
        {
            var pattern = $"%{EscapeLike(dto.Extension)}%";
            query = query.Where(file => EF.Functions.Like(file.Extension.Value, pattern, EscapeChar));
        }

        if (!string.IsNullOrWhiteSpace(dto.Mime))
        {
            var pattern = $"%{EscapeLike(dto.Mime)}%";
            query = query.Where(file => EF.Functions.Like(file.Mime.Value, pattern, EscapeChar));
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
            query = query.Where(file => file.SearchIndex.IsStale == dto.IsIndexStale.Value);
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
                        && file.Validity.IssuedAt.Value <= referenceInstant
                        && file.Validity.ValidUntil.Value >= referenceInstant);
                }
                else
                {
                    query = query.Where(file => file.Validity == null
                        || file.Validity.ValidUntil.Value < referenceInstant
                        || file.Validity.IssuedAt.Value > referenceInstant);
                }
            }

            if (dto.ExpiringInDays.HasValue)
            {
                var days = dto.ExpiringInDays.Value;
                var horizon = referenceInstant.AddDays(days);
                query = query.Where(file => file.Validity != null
                    && file.Validity.ValidUntil.Value >= referenceInstant
                    && file.Validity.ValidUntil.Value <= horizon);
            }
        }

        if (dto.SizeMin.HasValue)
        {
            query = query.Where(file => file.Size.Value >= dto.SizeMin.Value);
        }

        if (dto.SizeMax.HasValue)
        {
            query = query.Where(file => file.Size.Value <= dto.SizeMax.Value);
        }

        if (dto.CreatedFromUtc.HasValue)
        {
            var from = dto.CreatedFromUtc.Value.ToUniversalTime();
            query = query.Where(file => file.CreatedUtc.Value >= from);
        }

        if (dto.CreatedToUtc.HasValue)
        {
            var to = dto.CreatedToUtc.Value.ToUniversalTime();
            query = query.Where(file => file.CreatedUtc.Value <= to);
        }

        if (dto.ModifiedFromUtc.HasValue)
        {
            var from = dto.ModifiedFromUtc.Value.ToUniversalTime();
            query = query.Where(file => file.LastModifiedUtc.Value >= from);
        }

        if (dto.ModifiedToUtc.HasValue)
        {
            var to = dto.ModifiedToUtc.Value.ToUniversalTime();
            query = query.Where(file => file.LastModifiedUtc.Value <= to);
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
                    && file.Validity.IssuedAt.Value <= referenceInstant
                    && file.Validity.ValidUntil.Value >= referenceInstant);
            case ValidityFilterMode.Expired:
                return query.Where(file => file.Validity != null
                    && file.Validity.ValidUntil.Value < referenceInstant);
            case ValidityFilterMode.ExpiringWithin:
                if (!dto.ValidityFilterValue.HasValue)
                {
                    return query;
                }

                {
                    var horizon = AddRelative(referenceInstant, dto.ValidityFilterUnit, dto.ValidityFilterValue.Value);
                    return query.Where(file => file.Validity != null
                        && file.Validity.ValidUntil.Value >= referenceInstant
                        && file.Validity.ValidUntil.Value <= horizon);
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
                        && file.Validity.ValidUntil.Value >= start
                        && file.Validity.ValidUntil.Value <= end);
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
            return query.OrderBy(file => EF.Property<string>(file, nameof(FileEntity.Name)))
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
                "name" => ApplyOrder(orderedQuery, file => EF.Property<string>(file, nameof(FileEntity.Name)), spec.Descending, ref ordered),
                "mime" => ApplyOrder(orderedQuery, file => EF.Property<string>(file, nameof(FileEntity.Mime)), spec.Descending, ref ordered),
                "extension" => ApplyOrder(orderedQuery, file => EF.Property<string>(file, nameof(FileEntity.Extension)), spec.Descending, ref ordered),
                "size" => ApplyOrder(orderedQuery, file => EF.Property<long>(file, nameof(FileEntity.Size)), spec.Descending, ref ordered),
                "createdutc" => ApplyOrder(orderedQuery, file => EF.Property<string>(file, nameof(FileEntity.CreatedUtc)), spec.Descending, ref ordered),
                "modifiedutc" => ApplyOrder(orderedQuery, file => EF.Property<string>(file, nameof(FileEntity.LastModifiedUtc)), spec.Descending, ref ordered),
                "version" => ApplyOrder(orderedQuery, file => file.ContentRevision, spec.Descending, ref ordered),
                "author" => ApplyOrder(orderedQuery, file => file.Author, spec.Descending, ref ordered),
                "validuntil" => ApplyOrder(
                    orderedQuery,
                    file => file.Validity == null ? (DateTimeOffset?)null : file.Validity.ValidUntil.Value,
                    spec.Descending,
                    ref ordered),
                _ => orderedQuery,
            };
        }

        if (!ordered)
        {
            orderedQuery = orderedQuery.OrderBy(file => EF.Property<string>(file, nameof(FileEntity.Name)));
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
