namespace Veriado.Application.Files.Contracts;

/// <summary>
/// Represents an editable snapshot of a file aggregate exposed to the presentation layer.
/// </summary>
public sealed class FileDetailDto
{
    public Guid Id { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string Extension { get; init; } = string.Empty;

    public string MimeType { get; init; } = string.Empty;

    public string? Author { get; init; }

    public bool IsReadOnly { get; init; }

    public long Size { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ModifiedAt { get; init; }

    public int Version { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }

    public bool HasValidity => ValidFrom is not null && ValidTo is not null;

    public string DisplayName => string.IsNullOrWhiteSpace(Extension)
        ? FileName
        : string.Create(FileName.Length + Extension.Length + 1, (FileName, Extension), static (span, tuple) =>
        {
            var (name, extension) = tuple;
            name.AsSpan().CopyTo(span);
            span[name.Length] = '.';
            extension.AsSpan().CopyTo(span[(name.Length + 1)..]);
        });
}
