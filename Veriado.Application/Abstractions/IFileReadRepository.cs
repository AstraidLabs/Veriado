using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Appl.Common;
using Veriado.Domain.Metadata;

namespace Veriado.Appl.Abstractions;

/// <summary>
/// Provides read-optimized access to file projections for query scenarios.
/// </summary>
public interface IFileReadRepository
{
    Task<FileDetailReadModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken);

    Task<Page<FileListItemReadModel>> ListAsync(PageRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<FileListItemReadModel>> ListExpiringAsync(DateTimeOffset validUntilUtc, CancellationToken cancellationToken);
}

public sealed record FileListItemReadModel(
    Guid Id,
    string Name,
    string Extension,
    string Mime,
    string Author,
    long SizeBytes,
    int Version,
    bool IsReadOnly,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastModifiedUtc,
    DateTimeOffset? ValidUntilUtc);

public sealed record FileDocumentValidityReadModel(
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ValidUntilUtc,
    bool HasPhysicalCopy,
    bool HasElectronicCopy);

public sealed record FileDetailReadModel(
    Guid Id,
    string Name,
    string Extension,
    string Mime,
    string Author,
    long SizeBytes,
    int Version,
    bool IsReadOnly,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastModifiedUtc,
    FileDocumentValidityReadModel? Validity,
    FileSystemMetadata SystemMetadata,
    IReadOnlyDictionary<string, string?> ExtendedMetadata);
