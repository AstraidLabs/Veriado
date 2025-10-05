using System;
using System.Collections.Generic;
using System.Linq;
using Veriado.Appl.Search;
using Xunit;

namespace Veriado.Application.Tests.Search;

public static class LuceneQueryBuilderTests
{
    [Fact]
    public static void BuildMatch_DropsReservedTokensFromRawInput()
    {
        var factory = new FakeAnalyzerFactory(text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var result = LuceneQueryBuilder.BuildMatch("alpha and beta", prefix: false, allTerms: false, factory);

        Assert.Equal("alpha OR beta", result);
    }

    [Fact]
    public static void BuildMatch_ReturnsEmptyWhenOnlyReservedTokensRemain()
    {
        var factory = new FakeAnalyzerFactory(text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var result = LuceneQueryBuilder.BuildMatch("and or not", prefix: false, allTerms: false, factory);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public static void BuildMatch_QuotesReservedTokensNotPresentInRawInput()
    {
        var factory = new FakeAnalyzerFactory(_ => new[] { "alpha", "and" });

        var result = LuceneQueryBuilder.BuildMatch("alpha", prefix: false, allTerms: false, factory);

        Assert.Equal("alpha OR \"and\"", result);
    }

    private sealed class FakeAnalyzerFactory : IAnalyzerFactory
    {
        private readonly Func<string, IEnumerable<string>> _tokenize;

        public FakeAnalyzerFactory(Func<string, IEnumerable<string>> tokenize)
        {
            _tokenize = tokenize;
        }

        public ITextAnalyzer Create(string? profileOrLang = null) => new FakeAnalyzer(_tokenize);

        public bool TryGetProfile(string profileOrLang, out AnalyzerProfile profile)
        {
            profile = default;
            return false;
        }

        private sealed class FakeAnalyzer : ITextAnalyzer
        {
            private readonly Func<string, IEnumerable<string>> _tokenize;

            public FakeAnalyzer(Func<string, IEnumerable<string>> tokenize)
            {
                _tokenize = tokenize;
            }

            public string Normalize(string text, string? profileOrLang = null) => text;

            public IEnumerable<string> Tokenize(string text, string? profileOrLang = null)
                => _tokenize(text) ?? Enumerable.Empty<string>();
        }
    }
}
