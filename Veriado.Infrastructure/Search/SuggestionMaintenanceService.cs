namespace Veriado.Infrastructure.Search;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using Veriado.Domain.Search;

/// <summary>
/// Harvests suggestion tokens from indexed documents and persists them to the suggestion store.
/// </summary>
internal sealed class SuggestionMaintenanceService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SuggestionMaintenanceService> _logger;

    public SuggestionMaintenanceService(ISqliteConnectionFactory connectionFactory, ILogger<SuggestionMaintenanceService> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ReplaceAsync(
        SearchDocument? previousDocument,
        SearchDocument currentDocument,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);

        if (previousDocument is null)
        {
            return UpsertAsync(currentDocument, cancellationToken);
        }

        return ReplaceInternalAsync(previousDocument, currentDocument, cancellationToken);
    }

    public async Task UpsertAsync(SearchDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var harvested = HarvestGrouped(document);

        if (harvested.Count == 0)
        {
            return;
        }

        await ApplyAsync(harvested, SuggestionOperation.Add, document.FileId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task PruneAsync(SearchDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var harvested = HarvestGrouped(document);

        if (harvested.Count == 0)
        {
            return;
        }

        await ApplyAsync(harvested, SuggestionOperation.Remove, document.FileId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ReplaceInternalAsync(
        SearchDocument previousDocument,
        SearchDocument currentDocument,
        CancellationToken cancellationToken)
    {
        await ApplyAsync(
            HarvestGrouped(previousDocument),
            SuggestionOperation.Remove,
            previousDocument.FileId,
            cancellationToken)
            .ConfigureAwait(false);
        await ApplyAsync(
            HarvestGrouped(currentDocument),
            SuggestionOperation.Add,
            currentDocument.FileId,
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ApplyAsync(
        IReadOnlyList<HarvestedEntry> harvested,
        SuggestionOperation operation,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            var connection = lease.Connection;
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);

            await using SqliteTransaction sqliteTransaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var entry in harvested)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = sqliteTransaction;
                if (operation == SuggestionOperation.Add)
                {
                    command.CommandText = "INSERT INTO suggestions(term, weight, lang, source_field) VALUES($term, $weight, $lang, $source) " +
                        "ON CONFLICT(term, lang, source_field) DO UPDATE SET weight = weight + excluded.weight;";
                }
                else
                {
                    command.CommandText = @"UPDATE suggestions
SET weight = CASE
    WHEN weight > $weight THEN weight - $weight
    ELSE 0
END
WHERE term = $term AND lang = $lang AND source_field = $source;";
                }

                command.Parameters.Add("$term", SqliteType.Text).Value = entry.Term;
                command.Parameters.Add("$weight", SqliteType.Real).Value = entry.Weight;
                command.Parameters.Add("$lang", SqliteType.Text).Value = "en";
                command.Parameters.Add("$source", SqliteType.Text).Value = entry.Source;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (operation == SuggestionOperation.Remove)
            {
                await using var cleanup = connection.CreateCommand();
                cleanup.Transaction = sqliteTransaction;
                cleanup.CommandText = "DELETE FROM suggestions WHERE weight <= 0;";
                await cleanup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "Failed to update suggestions for document {FileId}", fileId);
        }
    }

    private static List<HarvestedEntry> HarvestGrouped(SearchDocument document)
    {
        return Harvest(document)
            .GroupBy(entry => (entry.Term, entry.Source), TermSourceComparer.Instance)
            .Select(group => new HarvestedEntry(group.Key.Term, group.Key.Source, group.Sum(x => x.Weight)))
            .ToList();
    }

    private static IEnumerable<(string Term, double Weight, string Source)> Harvest(SearchDocument document)
    {
        foreach (var token in Tokenize(document.Title))
        {
            yield return (token, 5d, "title");
        }

        if (!string.IsNullOrWhiteSpace(document.Author))
        {
            foreach (var token in Tokenize(document.Author))
            {
                yield return (token, 3d, "author");
            }
        }

        foreach (var token in Tokenize(document.FileName))
        {
            yield return (token, 2d, "filename");
        }

        if (!string.IsNullOrWhiteSpace(document.MetadataText))
        {
            foreach (var token in Tokenize(document.MetadataText))
            {
                yield return (token, 1d, "metadata");
            }
        }
    }

    private enum SuggestionOperation
    {
        Add,
        Remove,
    }

    private sealed record HarvestedEntry(string Term, string Source, double Weight);

    private static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var builder = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                if (builder.Length > 1)
                {
                    yield return builder.ToString();
                }

                builder.Clear();
            }
        }

        if (builder.Length > 1)
        {
            yield return builder.ToString();
        }
    }

    private sealed class TermSourceComparer : IEqualityComparer<(string Term, string Source)>
    {
        public static TermSourceComparer Instance { get; } = new();

        public bool Equals((string Term, string Source) x, (string Term, string Source) y)
        {
            return string.Equals(x.Term, y.Term, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Term, string Source) obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Term, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Source, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
