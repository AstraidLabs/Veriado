using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Veriado.Appl.Search;
using Microsoft.Extensions.Options;
using Xunit;

namespace Veriado.Application.Tests.Search;

public static class TrigramQueryBuilderTests
{
    [Fact]
    public static void BuildTrigramMatch_QuotesReservedKeywords()
    {
        var builder = new TrigramQueryBuilder(Options.Create(new SearchOptions()));
        var result = builder.BuildTrigramMatch("and or not", requireAllTerms: false);

        Assert.Equal("\"and\" OR \"not\" OR \"or\"", result);
    }

    [Fact]
    public static void TryBuild_ReturnsQuotedExpressionForReservedKeyword()
    {
        var builder = new TrigramQueryBuilder(Options.Create(new SearchOptions()));
        var built = builder.TryBuild("AND", requireAllTerms: false, out var match);

        Assert.True(built);
        Assert.Equal("\"and\"", match);
    }

    [Fact]
    public static void BuildIndexEntry_DoesNotIncludeQuotes()
    {
        var builder = new TrigramQueryBuilder(Options.Create(new SearchOptions()));
        var entry = builder.BuildIndexEntry("and", "or", "not");

        Assert.DoesNotContain('"', entry);
        Assert.False(string.IsNullOrWhiteSpace(entry));
    }

    [Fact]
    public static async Task TrigramMatch_WithReservedKeyword_ReturnsResults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE VIRTUAL TABLE trigram USING fts5(token);";
            await create.ExecuteNonQueryAsync();
        }

        var builder = new TrigramQueryBuilder(Options.Create(new SearchOptions()));
        var indexEntry = builder.BuildIndexEntry("andromeda");
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO trigram(token) VALUES ($value);";
            insert.Parameters.AddWithValue("$value", indexEntry);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var failing = connection.CreateCommand())
        {
            failing.CommandText = "SELECT count(*) FROM trigram WHERE trigram MATCH $query;";
            failing.Parameters.AddWithValue("$query", "and");

            await Assert.ThrowsAsync<SqliteException>(() => failing.ExecuteScalarAsync());
        }

        var sanitizedQuery = builder.BuildTrigramMatch("and", requireAllTerms: false);

        await using (var passing = connection.CreateCommand())
        {
            passing.CommandText = "SELECT count(*) FROM trigram WHERE trigram MATCH $query;";
            passing.Parameters.AddWithValue("$query", sanitizedQuery);

            var count = (long)(await passing.ExecuteScalarAsync() ?? 0L);
            Assert.Equal(1L, count);
        }
    }
}
