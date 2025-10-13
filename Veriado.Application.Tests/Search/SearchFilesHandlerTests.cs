using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.Extensions.Options;
using Veriado.Appl.Search;
using Veriado.Appl.Search.Abstractions;
using Veriado.Appl.UseCases.Search;
using Veriado.Infrastructure.Search;
using Veriado.Domain.Search;

namespace Veriado.Application.Tests.Search;

public sealed class SearchFilesHandlerTests
{
    [Fact]
    public void NormalizeWildcardSegment_PreservesWhitespaceBetweenTokens()
    {
        // Arrange
        var analyzerOptions = new AnalyzerOptions
        {
            DefaultProfile = "cs",
            Profiles = new Dictionary<string, AnalyzerProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["cs"] = new()
                {
                    Name = "cs"
                }
            }
        };

        var analyzerFactory = new AnalyzerFactory(Options.Create(analyzerOptions));
        var searchOptions = new SearchOptions
        {
            Analyzer = analyzerOptions
        };

        var handler = new SearchFilesHandler(
            new StubSearchQueryService(),
            new MapperConfiguration(cfg => { }).CreateMapper(),
            analyzerFactory,
            searchOptions);

        var method = typeof(SearchFilesHandler)
            .GetMethod("NormalizeWildcardSegment", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not locate NormalizeWildcardSegment method.");

        // Act
        var result = method.Invoke(handler, new object[] { "Data Science" }) as string;

        // Assert
        Assert.Equal("data science", result);
    }

    private sealed class StubSearchQueryService : ISearchQueryService
    {
        public Task<IReadOnlyList<(Guid Id, double Score)>> SearchWithScoresAsync(
            SearchQueryPlan plan,
            int skip,
            int take,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(Guid, double)>>(Array.Empty<(Guid, double)>());

        public Task<int> CountAsync(SearchQueryPlan plan, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQueryPlan plan, int? limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());
    }
}
