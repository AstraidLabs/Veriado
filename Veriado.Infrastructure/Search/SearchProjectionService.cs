using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Files;

namespace Veriado.Infrastructure.Search;

public sealed class SearchProjectionService : IFileSearchProjection
{
    private readonly DbContext _db;

    public SearchProjectionService(DbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task UpsertAsync(FileEntity file, CancellationToken ct)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        var doc = file.ToSearchDocument();
        const string sql = @"
INSERT INTO search_document
(file_id, title, author, mime, metadata_text, metadata_json, created_utc, modified_utc, content_hash)
VALUES ($id, $title, $author, $mime, $mtext, $mjson, $c_utc, $m_utc, $hash)
ON CONFLICT(file_id) DO UPDATE SET
    title=excluded.title,
    author=excluded.author,
    mime=excluded.mime,
    metadata_text=excluded.metadata_text,
    metadata_json=excluded.metadata_json,
    created_utc=excluded.created_utc,
    modified_utc=excluded.modified_utc,
    content_hash=excluded.content_hash;";

        var p = new[]
        {
            new SqliteParameter("$id", doc.FileId.ToByteArray()),
            new SqliteParameter("$title", (object?)doc.Title ?? DBNull.Value),
            new SqliteParameter("$author", (object?)doc.Author ?? DBNull.Value),
            new SqliteParameter("$mime", doc.Mime),
            new SqliteParameter("$mtext", (object?)doc.MetadataText ?? DBNull.Value),
            new SqliteParameter("$mjson", (object?)doc.MetadataJson ?? DBNull.Value),
            new SqliteParameter("$c_utc", doc.CreatedUtc.ToString("O")),
            new SqliteParameter("$m_utc", doc.ModifiedUtc.ToString("O")),
            new SqliteParameter("$hash", doc.ContentHash)
        };

        await _db.Database.ExecuteSqlRawAsync(sql, p, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid fileId, CancellationToken ct)
    {
        const string sql = "DELETE FROM search_document WHERE file_id = $id;";
        var p = new SqliteParameter("$id", fileId.ToByteArray());
        await _db.Database.ExecuteSqlRawAsync(sql, new[] { p }, ct).ConfigureAwait(false);
    }
}
