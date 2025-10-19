using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common.Exceptions;
using Veriado.Appl.Search;
using Veriado.Domain.Files;
using Veriado.Domain.Search;
using Veriado.Infrastructure.Common;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Search;

public sealed class SearchProjectionService : IFileSearchProjection, IBatchFileSearchProjection
{
    private const string UpsertWithGuardSql = @"INSERT INTO search_document (
    file_id,
    title,
    author,
    mime,
    metadata_text,
    metadata_json,
    created_utc,
    modified_utc,
    content_hash,
    stored_content_hash,
    stored_token_hash)
VALUES (
    $file_id,
    $title,
    $author,
    $mime,
    $metadata_text,
    $metadata_json,
    $created_utc,
    $modified_utc,
    $content_hash,
    $stored_content_hash,
    $stored_token_hash)
ON CONFLICT(file_id) DO UPDATE SET
    title = excluded.title,
    author = excluded.author,
    mime = excluded.mime,
    metadata_text = excluded.metadata_text,
    metadata_json = excluded.metadata_json,
    created_utc = excluded.created_utc,
    modified_utc = excluded.modified_utc,
    content_hash = excluded.content_hash,
    stored_content_hash = excluded.stored_content_hash,
    stored_token_hash = excluded.stored_token_hash
WHERE (search_document.stored_content_hash IS NULL OR search_document.stored_content_hash = $expected_old_hash)
  AND (search_document.stored_token_hash IS NULL OR search_document.stored_token_hash = $expected_old_token_hash);";

    private const string UpsertWithoutGuardSql = @"INSERT INTO search_document (
    file_id,
    title,
    author,
    mime,
    metadata_text,
    metadata_json,
    created_utc,
    modified_utc,
    content_hash,
    stored_content_hash,
    stored_token_hash)
VALUES (
    $file_id,
    $title,
    $author,
    $mime,
    $metadata_text,
    $metadata_json,
    $created_utc,
    $modified_utc,
    $content_hash,
    $stored_content_hash,
    $stored_token_hash)
ON CONFLICT(file_id) DO UPDATE SET
    title = excluded.title,
    author = excluded.author,
    mime = excluded.mime,
    metadata_text = excluded.metadata_text,
    metadata_json = excluded.metadata_json,
    created_utc = excluded.created_utc,
    modified_utc = excluded.modified_utc,
    content_hash = excluded.content_hash,
    stored_content_hash = excluded.stored_content_hash,
    stored_token_hash = excluded.stored_token_hash;";

    private readonly DbContext _dbContext;
    private readonly IAnalyzerFactory _analyzerFactory;
    private readonly ILogger<SearchProjectionService> _logger;
    private readonly ISearchTelemetry? _telemetry;

    public SearchProjectionService(
        DbContext dbContext,
        IAnalyzerFactory analyzerFactory,
        ILogger<SearchProjectionService>? logger = null,
        ISearchTelemetry? telemetry = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _logger = logger ?? NullLogger<SearchProjectionService>.Instance;
        _telemetry = telemetry;
    }

    public async Task UpsertAsync(
        FileEntity file,
        string? expectedContentHash,
        string? expectedTokenHash,
        string? newContentHash,
        string? tokenHash,
        ISearchProjectionScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(scope);

        scope.EnsureActive();

        await scope.ExecuteAsync(
            async ct =>
            {
                if (!SqliteFulltextSupport.IsAvailable)
                {
                    return;
                }

                var document = file.ToSearchDocument();
                var normalizedTitle = NormalizeOptional(document.Title);
                var normalizedAuthor = NormalizeOptional(document.Author);
                var normalizedMetadataText = NormalizeOptional(document.MetadataText);

                var expectedHash = string.IsNullOrWhiteSpace(expectedContentHash)
                    ? null
                    : expectedContentHash;
                var expectedToken = string.IsNullOrWhiteSpace(expectedTokenHash)
                    ? null
                    : expectedTokenHash;
                var storedContentHash = string.IsNullOrWhiteSpace(newContentHash)
                    ? (string?)document.ContentHash
                    : newContentHash;
                var storedTokenHash = string.IsNullOrWhiteSpace(tokenHash)
                    ? null
                    : tokenHash;

                var commandResult = await ExecuteUpsertCoreAsync(
                        document,
                        normalizedTitle,
                        normalizedAuthor,
                        normalizedMetadataText,
                        expectedHash,
                        expectedToken,
                        storedContentHash,
                        storedTokenHash,
                        applyGuard: true,
                        ct)
                    .ConfigureAwait(false);

                if (commandResult.RowsAffected == 0)
                {
                    var current = await ReadStoredSignaturesAsync(document.FileId, ct).ConfigureAwait(false);

                    if (current is not null
                        && EqualsOrdinal(current.StoredContentHash, storedContentHash)
                        && EqualsOrdinal(current.StoredTokenHash, storedTokenHash))
                    {
                        throw new AnalyzerOrContentDriftException();
                    }

                    throw new StaleSearchProjectionUpdateException();
                }
            },
            cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ForceReplaceAsync(
        FileEntity file,
        string? newContentHash,
        string? tokenHash,
        ISearchProjectionScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(scope);

        scope.EnsureActive();

        await scope.ExecuteAsync(
            async ct =>
            {
                if (!SqliteFulltextSupport.IsAvailable)
                {
                    return;
                }

                var document = file.ToSearchDocument();
                var normalizedTitle = NormalizeOptional(document.Title);
                var normalizedAuthor = NormalizeOptional(document.Author);
                var normalizedMetadataText = NormalizeOptional(document.MetadataText);

                var storedContentHash = string.IsNullOrWhiteSpace(newContentHash)
                    ? (string?)document.ContentHash
                    : newContentHash;
                var storedTokenHash = string.IsNullOrWhiteSpace(tokenHash)
                    ? null
                    : tokenHash;

                await ExecuteUpsertCoreAsync(
                        document,
                        normalizedTitle,
                        normalizedAuthor,
                        normalizedMetadataText,
                        expectedContentHash: null,
                        expectedTokenHash: null,
                        storedContentHash,
                        storedTokenHash,
                        applyGuard: false,
                        ct)
                    .ConfigureAwait(false);
            },
            cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SearchProjectionBatchResult> UpsertBatchAsync(
        IReadOnlyList<SearchProjectionWorkItem> items,
        ISearchProjectionScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(scope);

        scope.EnsureActive();

        var result = new SearchProjectionBatchResult(0);

        await scope.ExecuteAsync(
            async ct =>
            {
                if (!SqliteFulltextSupport.IsAvailable || items.Count == 0)
                {
                    result = new SearchProjectionBatchResult(0);
                    return;
                }

                var sqliteTransaction = GetAmbientTransaction();
                var connection = sqliteTransaction.Connection ?? throw new InvalidOperationException(
                    "Active SQLite transaction has no associated connection.");

                await using var guardedCommand = connection.CreateCommand();
                guardedCommand.Transaction = sqliteTransaction;
                guardedCommand.CommandText = UpsertWithGuardSql;
                EnsureSearchDocumentParameters(guardedCommand);

                await using var forceCommand = connection.CreateCommand();
                forceCommand.Transaction = sqliteTransaction;
                forceCommand.CommandText = UpsertWithoutGuardSql;
                EnsureSearchDocumentParameters(forceCommand);

                var busyRetries = 0;

                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();

                    var document = item.File.ToSearchDocument();
                    var normalizedTitle = NormalizeOptional(document.Title);
                    var normalizedAuthor = NormalizeOptional(document.Author);
                    var normalizedMetadataText = NormalizeOptional(document.MetadataText);

                    var expectedHash = string.IsNullOrWhiteSpace(item.ExpectedContentHash)
                        ? null
                        : item.ExpectedContentHash;
                    var expectedToken = string.IsNullOrWhiteSpace(item.ExpectedTokenHash)
                        ? null
                        : item.ExpectedTokenHash;
                    var storedContentHash = string.IsNullOrWhiteSpace(item.NewContentHash)
                        ? (string?)document.ContentHash
                        : item.NewContentHash;
                    var storedTokenHash = string.IsNullOrWhiteSpace(item.TokenHash)
                        ? null
                        : item.TokenHash;

                    var upsertResult = await ExecuteUpsertCoreAsync(
                            document,
                            normalizedTitle,
                            normalizedAuthor,
                            normalizedMetadataText,
                            expectedHash,
                            expectedToken,
                            storedContentHash,
                            storedTokenHash,
                            applyGuard: true,
                            ct,
                            guardedCommand)
                        .ConfigureAwait(false);

                    busyRetries += upsertResult.BusyRetries;

                    if (upsertResult.RowsAffected != 0)
                    {
                        continue;
                    }

                    var current = await ReadStoredSignaturesAsync(document.FileId, ct).ConfigureAwait(false);

                    if (current is not null
                        && EqualsOrdinal(current.StoredContentHash, storedContentHash)
                        && EqualsOrdinal(current.StoredTokenHash, storedTokenHash))
                    {
                        var forceResult = await ExecuteUpsertCoreAsync(
                                document,
                                normalizedTitle,
                                normalizedAuthor,
                                normalizedMetadataText,
                                expectedContentHash: null,
                                expectedTokenHash: null,
                                storedContentHash,
                                storedTokenHash,
                                applyGuard: false,
                                ct,
                                forceCommand)
                            .ConfigureAwait(false);

                        busyRetries += forceResult.BusyRetries;
                        continue;
                    }

                    throw new StaleSearchProjectionUpdateException();
                }

                result = new SearchProjectionBatchResult(busyRetries);
            },
            cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    private async Task<CommandExecutionResult> ExecuteUpsertCoreAsync(
        SearchDocument document,
        string? normalizedTitle,
        string? normalizedAuthor,
        string? normalizedMetadataText,
        string? expectedContentHash,
        string? expectedTokenHash,
        string? storedContentHash,
        string? storedTokenHash,
        bool applyGuard,
        CancellationToken cancellationToken,
        SqliteCommand? reusableCommand = null)
    {
        var sqliteTransaction = GetAmbientTransaction();
        var connection = sqliteTransaction.Connection ?? throw new InvalidOperationException(
            "Active SQLite transaction has no associated connection.");

        if (reusableCommand is not null)
        {
            return await ExecuteCommandAsync(reusableCommand).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        return await ExecuteCommandAsync(command).ConfigureAwait(false);

        async Task<CommandExecutionResult> ExecuteCommandAsync(SqliteCommand command)
        {
            command.Transaction = sqliteTransaction;
            command.CommandText = applyGuard ? UpsertWithGuardSql : UpsertWithoutGuardSql;
            EnsureSearchDocumentParameters(command);

            var contentHashValue = string.IsNullOrWhiteSpace(document.ContentHash)
                ? storedContentHash
                : document.ContentHash;

            ConfigureSearchDocumentParameters(
                command,
                document.FileId,
                normalizedTitle,
                normalizedAuthor,
                document.Mime,
                normalizedMetadataText,
                document.MetadataJson,
                document.CreatedUtc,
                document.ModifiedUtc,
                contentHashValue,
                storedContentHash,
                storedTokenHash,
                expectedContentHash,
                expectedTokenHash);

            var operation = applyGuard ? "updating" : "force updating";
            var busyRetries = 0;

            try
            {
                var rowsAffected = await SqliteRetry.ExecuteAsync(
                        () => command.ExecuteNonQueryAsync(cancellationToken),
                        (exception, attempt, delay) =>
                        {
                            busyRetries++;
                            _telemetry?.RecordSqliteBusyRetry(1);
                            _logger.LogWarning(
                                exception,
                                "SQLite busy while {Operation} search document for {FileId}; retrying in {Delay} (attempt {Attempt}/5)",
                                operation,
                                document.FileId,
                                delay,
                                attempt);
                            return Task.CompletedTask;
                        },
                        (exception, attempt) =>
                        {
                            busyRetries++;
                            _telemetry?.RecordSqliteBusyRetry(1);
                            _logger.LogError(
                                exception,
                                "SQLite busy while {Operation} search document for {FileId} after {Attempts} attempts; aborting.",
                                operation,
                                document.FileId,
                                attempt);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                return new CommandExecutionResult(rowsAffected, busyRetries);
            }
            catch (SqliteException ex)
            {
                LogSqliteFailure(ex, command);
                if (ex.IndicatesFatalFulltextFailure())
                {
                    throw new SearchIndexCorruptedException(
                        "SQLite full-text index became unavailable and needs to be repaired.",
                        ex);
                }

                throw;
            }
        }
    }

    public async Task DeleteAsync(
        Guid fileId,
        ISearchProjectionScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.EnsureActive();

        await scope.ExecuteAsync(
            async ct =>
            {
                if (!SqliteFulltextSupport.IsAvailable)
                {
                    return;
                }

                var sqliteTransaction = GetAmbientTransaction();
                var connection = sqliteTransaction.Connection ?? throw new InvalidOperationException(
                    "Active SQLite transaction has no associated connection.");

                SqliteCommand? activeCommand = null;

                try
                {
                    await using var delete = connection.CreateCommand();
                    delete.Transaction = sqliteTransaction;
                    delete.CommandText = "DELETE FROM search_document WHERE file_id = $file_id;";
                    delete.Parameters.Add("$file_id", SqliteType.Blob).Value = fileId.ToByteArray();
                    activeCommand = delete;
                    await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    activeCommand = null;
                }
                catch (SqliteException ex)
                {
                    LogSqliteFailure(ex, activeCommand);
                    if (ex.IndicatesFatalFulltextFailure())
                    {
                        throw new SearchIndexCorruptedException(
                            "SQLite full-text index became unavailable and needs to be repaired.",
                            ex);
                    }

                    throw;
                }
            },
            cancellationToken)
            .ConfigureAwait(false);
    }

    private SqliteTransaction GetAmbientTransaction()
    {
        var transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Search projection operations require an active EF Core transaction.");

        if (transaction is not SqliteTransaction sqliteTransaction)
        {
            throw new InvalidOperationException("Search projection operations require a SQLite transaction.");
        }

        return sqliteTransaction;
    }

    private string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : TextNormalization.NormalizeText(value, _analyzerFactory);
    }

    private static void ConfigureSearchDocumentParameters(
        SqliteCommand command,
        Guid fileId,
        string? normalizedTitle,
        string? normalizedAuthor,
        string mime,
        string? normalizedMetadataText,
        string? metadataJson,
        DateTimeOffset createdUtc,
        DateTimeOffset modifiedUtc,
        string? contentHash,
        string? storedContentHash,
        string? storedTokenHash,
        string? expectedOldHash,
        string? expectedOldTokenHash)
    {
        if (string.IsNullOrEmpty(mime))
        {
            throw new ArgumentException("MIME type is required for search_document writes.", nameof(mime));
        }

        EnsureSearchDocumentParameters(command);
        command.Parameters["$file_id"].Value = fileId.ToByteArray();
        command.Parameters["$title"].Value = (object?)normalizedTitle ?? DBNull.Value;
        command.Parameters["$author"].Value = (object?)normalizedAuthor ?? DBNull.Value;
        command.Parameters["$mime"].Value = mime;
        command.Parameters["$metadata_text"].Value = (object?)normalizedMetadataText ?? DBNull.Value;
        command.Parameters["$metadata_json"].Value = (object?)metadataJson ?? DBNull.Value;
        command.Parameters["$created_utc"].Value = createdUtc.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters["$modified_utc"].Value = modifiedUtc.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters["$content_hash"].Value = (object?)contentHash ?? DBNull.Value;
        command.Parameters["$stored_content_hash"].Value = (object?)storedContentHash ?? DBNull.Value;
        command.Parameters["$stored_token_hash"].Value = (object?)storedTokenHash ?? DBNull.Value;
        command.Parameters["$expected_old_hash"].Value = (object?)expectedOldHash ?? DBNull.Value;
        command.Parameters["$expected_old_token_hash"].Value = (object?)expectedOldTokenHash ?? DBNull.Value;
    }

    private static void EnsureSearchDocumentParameters(SqliteCommand command)
    {
        if (command.Parameters.Count > 0)
        {
            return;
        }

        command.Parameters.Add("$file_id", SqliteType.Blob);
        command.Parameters.Add("$title", SqliteType.Text);
        command.Parameters.Add("$author", SqliteType.Text);
        command.Parameters.Add("$mime", SqliteType.Text);
        command.Parameters.Add("$metadata_text", SqliteType.Text);
        command.Parameters.Add("$metadata_json", SqliteType.Text);
        command.Parameters.Add("$created_utc", SqliteType.Text);
        command.Parameters.Add("$modified_utc", SqliteType.Text);
        command.Parameters.Add("$content_hash", SqliteType.Text);
        command.Parameters.Add("$stored_content_hash", SqliteType.Text);
        command.Parameters.Add("$stored_token_hash", SqliteType.Text);
        command.Parameters.Add("$expected_old_hash", SqliteType.Text);
        command.Parameters.Add("$expected_old_token_hash", SqliteType.Text);
    }

    private async Task<StoredSignatures?> ReadStoredSignaturesAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var sqliteTransaction = GetAmbientTransaction();
        var connection = sqliteTransaction.Connection ?? throw new InvalidOperationException(
            "Active SQLite transaction has no associated connection.");

        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText = "SELECT stored_content_hash, stored_token_hash FROM search_document WHERE file_id = $file_id LIMIT 1;";
        command.Parameters.Add("$file_id", SqliteType.Blob).Value = fileId.ToByteArray();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var storedContentHash = reader.IsDBNull(0) ? null : reader.GetString(0);
        var storedTokenHash = reader.IsDBNull(1) ? null : reader.GetString(1);
        return new StoredSignatures(storedContentHash, storedTokenHash);
    }

    private static bool EqualsOrdinal(string? left, string? right)
        => string.Equals(left, right, StringComparison.Ordinal);

    private sealed record StoredSignatures(string? StoredContentHash, string? StoredTokenHash);

    private readonly record struct CommandExecutionResult(int RowsAffected, int BusyRetries);

    private void LogSqliteFailure(SqliteException exception, SqliteCommand? command)
    {
        if (!_logger.IsEnabled(LogLevel.Error))
        {
            return;
        }

        var builder = new StringBuilder();

        if (command is not null)
        {
            builder.AppendLine("CommandText:");
            builder.AppendLine(command.CommandText);
            builder.AppendLine("Parameters:");

            foreach (SqliteParameter parameter in command.Parameters)
            {
                builder
                    .Append("  ")
                    .Append(parameter.ParameterName)
                    .Append(" = ")
                    .Append(FormatParameterValue(parameter.Value))
                    .AppendLine();
            }
        }

        var snapshot = SqliteFulltextSupport.SchemaSnapshot;
        var mode = snapshot?.IsContentless switch
        {
            true => "contentless",
            false => "content-linked",
            _ => "unknown",
        };

        var triggerSummary = snapshot?.HasSearchDocumentTriggers == true
            ? string.Join(", ", snapshot.Triggers.Keys)
            : "missing";

        builder.Append("FTS schema mode=")
            .Append(mode)
            .Append(", triggers=")
            .Append(triggerSummary)
            .Append(", lastChecked=")
            .Append(snapshot?.CheckedAtUtc.ToString("O", CultureInfo.InvariantCulture) ?? "<never>");

        var reason = SqliteFulltextSupport.FailureReason;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.Append(", failureReason=").Append(reason);
        }

        _logger.LogError(exception, "SQLite FTS command failed. {Details}", builder.ToString());
    }

    private static string FormatParameterValue(object? value)
    {
        return value switch
        {
            null or DBNull => "NULL",
            byte[] blob => $"BLOB(length={blob.Length})",
            string text when text.Length > 256 => $"TEXT(len={text.Length})",
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<unrepresentable>",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<unrepresentable>",
        };
    }
}

internal interface IBatchFileSearchProjection
{
    Task<SearchProjectionBatchResult> UpsertBatchAsync(
        IReadOnlyList<SearchProjectionWorkItem> items,
        ISearchProjectionScope scope,
        CancellationToken cancellationToken);
}

internal readonly record struct SearchProjectionBatchResult(int BusyRetries);

internal readonly record struct SearchProjectionWorkItem(
    FileEntity File,
    string? ExpectedContentHash,
    string? ExpectedTokenHash,
    string? NewContentHash,
    string? TokenHash,
    SearchIndexSignature Signature,
    DateTimeOffset? IndexedUtc,
    string? IndexedTitle);
