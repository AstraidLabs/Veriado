using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Contracts.Import;

namespace Veriado.Application.Import;

public interface IFileImportWriter
{
    Task<ImportResult> ImportAsync(
        IReadOnlyList<ImportItem> items,
        ImportOptions options,
        CancellationToken ct);
}

public sealed record ImportItem(
    Guid FileId,
    string Name,
    string? Extension,
    string Mime,
    long SizeBytes,
    string Hash,
    string StorageProvider,
    string StoragePath,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    object? Metadata);

public sealed record ImportOptions(
    int BatchSize = 500,
    bool UpsertFts = true,
    bool DetachAfterBatch = true,
    PerformanceProfile PerformanceProfile = PerformanceProfile.Normal,
    int MaxDegreeOfParallelism = 1);

public sealed record ImportResult(int Imported, int Skipped, int Updated);

public sealed record ImportMetadata(
    string Author,
    string? Title,
    bool IsReadOnly,
    int Version,
    int LinkedContentVersion,
    Guid? FileSystemId,
    ImportFileSystemMetadata? FileSystem,
    ImportValidity? Validity,
    ImportSearchMetadata? Search,
    ImportFtsPolicy? FtsPolicy);

public sealed record ImportFileSystemMetadata(
    int Attributes,
    string? OwnerSid,
    bool IsEncrypted,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? LastWriteUtc,
    DateTimeOffset? LastAccessUtc,
    uint? HardLinkCount,
    uint? AlternateDataStreamCount);

public sealed record ImportValidity(
    DateTimeOffset IssuedAt,
    DateTimeOffset ValidUntil,
    bool HasPhysicalCopy,
    bool HasElectronicCopy);

public sealed record ImportSearchMetadata(
    int SchemaVersion,
    bool IsStale,
    DateTimeOffset? IndexedUtc,
    string? IndexedTitle,
    string? IndexedContentHash,
    string? AnalyzerVersion,
    string? TokenHash);

public sealed record ImportFtsPolicy(bool RemoveDiacritics, string Tokenizer, string TokenChars);
