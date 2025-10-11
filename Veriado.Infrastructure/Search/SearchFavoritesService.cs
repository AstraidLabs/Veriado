using Microsoft.Data.Sqlite;
using Veriado.Contracts.Search;

namespace Veriado.Infrastructure.Search;

internal sealed class SearchFavoritesService : ISearchFavoritesService
{
    private readonly InfrastructureOptions _options;
    private readonly IClock _clock;

    public SearchFavoritesService(InfrastructureOptions options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    public async Task<IReadOnlyList<SearchFavoriteItem>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name, query_text, match, position, created_utc, is_fuzzy " +
            "FROM search_favorites ORDER BY position ASC;";

        var favorites = new List<SearchFavoriteItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = new Guid((byte[])reader[0]);
            var name = reader.GetString(1);
            var queryText = reader.IsDBNull(2) ? null : reader.GetString(2);
            var match = reader.GetString(3);
            var position = reader.GetInt32(4);
            var createdUtc = DateTimeOffset.Parse(reader.GetString(5), null, DateTimeStyles.RoundtripKind);
            var isFuzzy = !reader.IsDBNull(6) && reader.GetBoolean(6);
            favorites.Add(new SearchFavoriteItem(id, name, queryText, match, position, createdUtc, isFuzzy));
        }

        return favorites;
    }

    public async Task AddAsync(string name, string matchQuery, string? queryText, bool isFuzzy, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(matchQuery);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);

        long maxPosition;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COALESCE(MAX(position), -1) FROM search_favorites;";
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            maxPosition = result is long value ? value : -1;
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO search_favorites(id, name, query_text, match, position, created_utc, is_fuzzy) " +
            "VALUES ($id, $name, $queryText, $match, $position, $createdUtc, $isFuzzy);";
        insert.Parameters.Add("$id", SqliteType.Blob).Value = Guid.NewGuid().ToByteArray();
        insert.Parameters.Add("$name", SqliteType.Text).Value = name;
        insert.Parameters.Add("$queryText", SqliteType.Text).Value = (object?)queryText ?? DBNull.Value;
        insert.Parameters.Add("$match", SqliteType.Text).Value = matchQuery;
        insert.Parameters.Add("$position", SqliteType.Integer).Value = maxPosition + 1;
        insert.Parameters.Add("$createdUtc", SqliteType.Text).Value = _clock.UtcNow.ToString("O");
        insert.Parameters.Add("$isFuzzy", SqliteType.Integer).Value = isFuzzy ? 1 : 0;
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameAsync(Guid id, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE search_favorites SET name = $name WHERE id = $id;";
        command.Parameters.Add("$name", SqliteType.Text).Value = newName;
        command.Parameters.Add("$id", SqliteType.Blob).Value = id.ToByteArray();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);

        var providedOrder = orderedIds?.ToList() ?? new List<Guid>();
        var known = new HashSet<Guid>(providedOrder);
        var existing = new List<Guid>();

        await using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT id FROM search_favorites ORDER BY position ASC;";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = new Guid((byte[])reader[0]);
                if (!known.Contains(id))
                {
                    existing.Add(id);
                }
            }
        }

        providedOrder.AddRange(existing);

        await using var sqliteTransaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        for (var index = 0; index < providedOrder.Count; index++)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = sqliteTransaction;
            update.CommandText = "UPDATE search_favorites SET position = $position WHERE id = $id;";
            update.Parameters.Add("$position", SqliteType.Integer).Value = index;
            update.Parameters.Add("$id", SqliteType.Blob).Value = providedOrder[index].ToByteArray();
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM search_favorites WHERE id = $id;";
        command.Parameters.Add("$id", SqliteType.Blob).Value = id.ToByteArray();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SearchFavoriteItem?> TryGetByKeyAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name, query_text, match, position, created_utc, is_fuzzy " +
            "FROM search_favorites WHERE name = $name LIMIT 1;";
        command.Parameters.Add("$name", SqliteType.Text).Value = key;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var id = new Guid((byte[])reader[0]);
        var name = reader.GetString(1);
        var queryText = reader.IsDBNull(2) ? null : reader.GetString(2);
        var match = reader.GetString(3);
        var position = reader.GetInt32(4);
        var createdUtc = DateTimeOffset.Parse(reader.GetString(5), null, DateTimeStyles.RoundtripKind);
        var isFuzzy = !reader.IsDBNull(6) && reader.GetBoolean(6);
        return new SearchFavoriteItem(id, name, queryText, match, position, createdUtc, isFuzzy);
    }

    private SqliteConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
        }

        return new SqliteConnection(_options.ConnectionString);
    }
}
