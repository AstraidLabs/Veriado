#r "nuget: Microsoft.Data.Sqlite, 8.0.4"

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

if (Args.Length == 0)
{
    Console.WriteLine("Usage: dotnet script tools/fts5-benchmark.csx -- <databasePath> [query1] [query2] ...");
    Console.WriteLine("Example: dotnet script tools/fts5-benchmark.csx -- veriado.db \"title:report\" \"author:\\\\\"Smith\\\\\\"\"");
    return;
}

var databasePath = Args[0];
var queries = Args.Skip(1).ToArray();

var connectionStringBuilder = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    Cache = SqliteCacheMode.Shared,
    Mode = SqliteOpenMode.ReadWriteCreate,
};

await using var connection = new SqliteConnection(connectionStringBuilder.ConnectionString);
await connection.OpenAsync();
await ApplyPragmasAsync(connection);

Console.WriteLine($"Connected to {connection.DataSource}. Starting benchmark…\n");

var (throughput, elapsedMs) = await MeasureIndexingThroughputAsync(connection, batchSize: 400);
Console.WriteLine($"Indexing throughput: {throughput:F2} rows/s (batch 400 in {elapsedMs:F2} ms)");

if (queries.Length == 0)
{
    Console.WriteLine("No query latency benchmark executed (no queries supplied). Pass queries as extra arguments to measure MATCH performance.");
    return;
}

foreach (var query in queries)
{
    var latencies = await MeasureQueryLatenciesAsync(connection, query, iterations: 50);
    if (latencies.Count == 0)
    {
        Console.WriteLine($"Query '{query}' returned no samples.");
        continue;
    }

    var (p50, p95) = CalculatePercentiles(latencies);
    Console.WriteLine($"Query '{query}' → p50={p50:F2} ms | p95={p95:F2} ms (samples={latencies.Count})");
}

static async Task ApplyPragmasAsync(SqliteConnection connection)
{
    await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;");
    await ExecuteAsync(connection, "PRAGMA synchronous=NORMAL;");
    await ExecuteAsync(connection, "PRAGMA busy_timeout=8000;");
    await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;");
    await ExecuteAsync(connection, "PRAGMA temp_store=MEMORY;");
    await ExecuteAsync(connection, "PRAGMA page_size=4096;");
    await ExecuteAsync(connection, "PRAGMA mmap_size=268435456;");
    await ExecuteAsync(connection, "PRAGMA cache_size=-32768;");

    static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

static async Task<(double Throughput, double ElapsedMs)> MeasureIndexingThroughputAsync(SqliteConnection connection, int batchSize)
{
    var random = new Random();
    var stopwatch = Stopwatch.StartNew();

    await using var transaction = await connection.BeginTransactionAsync();
    try
    {
        for (var i = 0; i < batchSize; i++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO DocumentContent (FileId, Title, Author, Mime, MetadataText, Metadata)
VALUES ($fileId, $title, $author, $mime, $metadataText, $metadata)
ON CONFLICT(FileId) DO UPDATE SET
    Title = excluded.Title,
    Author = excluded.Author,
    Mime = excluded.Mime,
    MetadataText = excluded.MetadataText,
    Metadata = excluded.Metadata;";

            var guid = Guid.NewGuid();
            command.Parameters.Add("$fileId", SqliteType.Blob).Value = guid.ToByteArray();
            command.Parameters.AddWithValue("$title", $"Synthetic benchmark document #{i}");
            command.Parameters.AddWithValue("$author", $"Test Author {random.Next(1, 100)}");
            command.Parameters.AddWithValue("$mime", "application/octet-stream");
            command.Parameters.AddWithValue("$metadataText", $"Synthetic metadata summary {i}");
            command.Parameters.AddWithValue("$metadata", $"{{\"index\":{i}}}");

            await command.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        await transaction.RollbackAsync();
    }

    stopwatch.Stop();
    var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
    var throughput = batchSize / stopwatch.Elapsed.TotalSeconds;
    return (throughput, elapsedMs);
}

static async Task<List<double>> MeasureQueryLatenciesAsync(SqliteConnection connection, string query, int iterations)
{
    var latencies = new List<double>(iterations);

    // Warm-up to mitigate cold cache effects.
    await using (var warmup = connection.CreateCommand())
    {
        warmup.CommandText = "SELECT 1 FROM file_search WHERE file_search MATCH $q LIMIT 1;";
        warmup.Parameters.Add("$q", SqliteType.Text).Value = query;
        await warmup.ExecuteScalarAsync();
    }

    await using var command = connection.CreateCommand();
    var sqlBuilder = new StringBuilder();
    sqlBuilder.Append("SELECT dc.FileId FROM file_search s ");
    sqlBuilder.Append("JOIN DocumentContent dc ON dc.DocId = s.rowid ");
    sqlBuilder.Append("WHERE s MATCH $query LIMIT 25;");

    command.CommandText = sqlBuilder.ToString();
    command.Parameters.Add("$query", SqliteType.Text).Value = query;

    for (var i = 0; i < iterations; i++)
    {
        var stopwatch = Stopwatch.StartNew();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // Drain result set to ensure full query execution.
        }

        stopwatch.Stop();
        latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
    }

    return latencies;
}

static (double P50, double P95) CalculatePercentiles(List<double> samples)
{
    if (samples.Count == 0)
    {
        return (0d, 0d);
    }

    var ordered = samples.OrderBy(static value => value).ToArray();
    var p50 = Percentile(ordered, 0.50);
    var p95 = Percentile(ordered, 0.95);
    return (p50, p95);

    static double Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        if (ordered.Count == 1)
        {
            return ordered[0];
        }

        var position = percentile * (ordered.Count - 1);
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);
        if (lowerIndex == upperIndex)
        {
            return ordered[lowerIndex];
        }

        var weight = position - lowerIndex;
        return ordered[lowerIndex] * (1 - weight) + ordered[upperIndex] * weight;
    }
}
