using System;
using Veriado.Appl.Search;
using Veriado.Domain.Search;
using Xunit;

namespace Veriado.Application.Tests.Search;

public static class SearchQueryBuilderTests
{
    [Fact]
    public static void Range_ModifiedDate_AddsNumericRangeFilter()
    {
        var builder = new SearchQueryBuilder();
        var root = builder.Term(null, "report");
        var from = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        builder.Range("modified", from, null);
        var plan = builder.Build(root, "report");

        var filter = Assert.Single(plan.RangeFilters);
        Assert.Equal(SearchFieldNames.ModifiedTicks, filter.Field);
        Assert.Equal(SearchRangeValueKind.Numeric, filter.ValueKind);
        Assert.Equal(from.UtcTicks, filter.LowerValue);
        Assert.True(filter.IncludeLower);
        Assert.Null(filter.UpperValue);
        Assert.False(filter.IncludeUpper);
    }

    [Fact]
    public static void Range_SizeBytes_PopulatesBothBounds()
    {
        var builder = new SearchQueryBuilder();
        var root = builder.Term(null, "log");

        builder.Range("size", 1024, 4096);
        var plan = builder.Build(root, "log");

        var filter = Assert.Single(plan.RangeFilters);
        Assert.Equal(SearchFieldNames.SizeBytes, filter.Field);
        Assert.Equal(1024L, filter.LowerValue);
        Assert.Equal(4096L, filter.UpperValue);
        Assert.True(filter.IncludeLower);
        Assert.True(filter.IncludeUpper);
    }
}
