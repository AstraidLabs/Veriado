using System;
using Veriado.Domain.Files.Events;
using Veriado.Domain.Primitives;
using Veriado.Domain.Search;
using Veriado.Domain.Search.Events;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files;

/// <summary>
/// Aggregate root representing a managed file with metadata, content, and validity information.
/// </summary>
public sealed class FileEntity : AggregateRoot
{
    private const int InitialVersion = 1;
    private string _author;

    private FileEntity(
        Guid id,
        FileName name,
        FileExtension extension,
        MimeType mime,
        string author,
        FileContentEntity content,
        DateTimeOffset createdUtc,
        Fts5Policy ftsPolicy)
        : base(id)
    {
        Name = name;
        Extension = extension;
        Mime = mime;
        _author = author;
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Size = content.Size;
        Version = InitialVersion;
        IsReadOnly = false;
        CreatedUtc = createdUtc.ToUniversalTime();
        LastModifiedUtc = CreatedUtc;
        SearchIndex = new SearchIndexState(isStale: true);
        FtsPolicy = ftsPolicy ?? throw new ArgumentNullException(nameof(ftsPolicy));
    }

    /// <summary>
    /// Gets the file name.
    /// </summary>
    public FileName Name { get; private set; }

    /// <summary>
    /// Gets the file extension.
    /// </summary>
    public FileExtension Extension { get; private set; }

    /// <summary>
    /// Gets the MIME type.
    /// </summary>
    public MimeType Mime { get; private set; }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public ByteSize Size { get; private set; }

    /// <summary>
    /// Gets the author metadata.
    /// </summary>
    public string Author => _author;

    /// <summary>
    /// Gets a value indicating whether the file is read-only.
    /// </summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// Gets the content version number. Starts at 1 and increments on each replacement.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Gets the creation timestamp in UTC.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; }

    /// <summary>
    /// Gets the last modification timestamp in UTC.
    /// </summary>
    public DateTimeOffset LastModifiedUtc { get; private set; }

    /// <summary>
    /// Gets the associated file content (1:1 relationship).
    /// </summary>
    public FileContentEntity Content { get; private set; }

    /// <summary>
    /// Gets the associated validity entity, if present (0..1:1 relationship).
    /// </summary>
    public FileDocumentValidityEntity? Validity { get; private set; }

    /// <summary>
    /// Gets the search indexing state for full-text readiness.
    /// </summary>
    public SearchIndexState SearchIndex { get; }

    /// <summary>
    /// Gets the full-text policy hints.
    /// </summary>
    public Fts5Policy FtsPolicy { get; }

    /// <summary>
    /// Creates a new file aggregate root.
    /// </summary>
    public static FileEntity CreateNew(
        string name,
        string extension,
        string mime,
        string author,
        ReadOnlySpan<byte> bytes,
        int? maxContentSize = null,
        Fts5Policy? ftsPolicy = null)
    {
        var fileName = FileName.From(name);
        var fileExtension = FileExtension.From(extension);
        var mimeType = MimeType.From(mime);
        var normalizedAuthor = NormalizeAuthor(author);
        var policy = ftsPolicy ?? Fts5Policy.Default;
        var id = Guid.NewGuid();
        var content = FileContentEntity.FromBytes(id, bytes, maxContentSize);
        var createdUtc = DateTimeOffset.UtcNow;

        var file = new FileEntity(id, fileName, fileExtension, mimeType, normalizedAuthor, content, createdUtc, policy);

        file.RaiseDomainEvent(new FileCreated(id, fileName, fileExtension, mimeType, normalizedAuthor, content.Hash, content.Size, createdUtc));
        file.MarkSearchDirty(SearchReindexReason.Created);

        return file;
    }

    /// <summary>
    /// Renames the file (optionally changing extension).
    /// </summary>
    /// <param name="name">New name.</param>
    /// <param name="extension">Optional new extension; when null the current extension is preserved.</param>
    public void Rename(string name, string? extension = null)
    {
        EnsureMutable();

        var newName = FileName.From(name);
        var newExtension = extension is null ? Extension : FileExtension.From(extension);

        if (newName == Name && newExtension == Extension)
        {
            return;
        }

        var oldName = Name;
        var oldExtension = Extension;
        Name = newName;
        Extension = newExtension;

        Touch();
        RaiseDomainEvent(new FileRenamed(Id, oldName, oldExtension, Name, Extension, LastModifiedUtc));
        MarkSearchDirty(SearchReindexReason.MetadataChanged);
    }

    /// <summary>
    /// Updates mutable metadata such as MIME type and author.
    /// </summary>
    /// <param name="mime">Optional MIME type override.</param>
    /// <param name="author">Optional author override.</param>
    public void UpdateMetadata(string? mime = null, string? author = null)
    {
        EnsureMutable();

        var oldMime = Mime;
        var oldAuthor = _author;

        var changed = false;

        if (mime is not null)
        {
            var newMime = MimeType.From(mime);
            if (newMime != Mime)
            {
                Mime = newMime;
                changed = true;
            }
        }

        if (author is not null)
        {
            var newAuthor = NormalizeAuthor(author);
            if (!string.Equals(newAuthor, _author, StringComparison.Ordinal))
            {
                _author = newAuthor;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        Touch();
        RaiseDomainEvent(new FileMetadataUpdated(Id, oldMime, Mime, oldAuthor, _author, LastModifiedUtc));
        MarkSearchDirty(SearchReindexReason.MetadataChanged);
    }

    /// <summary>
    /// Replaces the file content, enforcing read-only and version invariants.
    /// </summary>
    /// <param name="bytes">New binary content.</param>
    /// <param name="maxContentSize">Optional maximum allowed size.</param>
    public void ReplaceContent(ReadOnlySpan<byte> bytes, int? maxContentSize = null)
    {
        EnsureMutable();

        var newContent = FileContentEntity.FromBytes(Id, bytes, maxContentSize);

        if (newContent.Hash == Content.Hash && newContent.Size == Content.Size)
        {
            return;
        }

        if (Version == int.MaxValue)
        {
            throw new InvalidOperationException("File version cannot exceed Int32.MaxValue.");
        }

        var oldHash = Content.Hash;
        var oldSize = Content.Size;

        Content = newContent;
        Size = newContent.Size;
        Version++;

        Touch();
        RaiseDomainEvent(new FileContentReplaced(Id, oldHash, newContent.Hash, oldSize, newContent.Size, Version, LastModifiedUtc));
        MarkSearchDirty(SearchReindexReason.ContentChanged);
    }

    /// <summary>
    /// Sets or clears the read-only flag.
    /// </summary>
    /// <param name="isReadOnly">Desired read-only state.</param>
    public void SetReadOnly(bool isReadOnly)
    {
        if (IsReadOnly == isReadOnly)
        {
            return;
        }

        IsReadOnly = isReadOnly;
        Touch();
        RaiseDomainEvent(new FileReadOnlyChanged(Id, IsReadOnly, LastModifiedUtc));
    }

    /// <summary>
    /// Adds or updates the document validity information.
    /// </summary>
    public void SetValidity(DateTimeOffset issuedAtUtc, DateTimeOffset validUntilUtc, bool hasPhysicalCopy, bool hasElectronicCopy)
    {
        EnsureMutable();

        var issued = issuedAtUtc.ToUniversalTime();
        var valid = validUntilUtc.ToUniversalTime();

        var changed = false;

        if (Validity is null)
        {
            Validity = FileDocumentValidityEntity.Create(Id, issued, valid, hasPhysicalCopy, hasElectronicCopy);
            changed = true;
        }
        else
        {
            var oldIssued = Validity.IssuedAtUtc;
            var oldValid = Validity.ValidUntilUtc;
            var oldPhysical = Validity.HasPhysicalCopy;
            var oldElectronic = Validity.HasElectronicCopy;

            Validity.SetPeriod(issued, valid);
            Validity.SetCopies(hasPhysicalCopy, hasElectronicCopy);

            if (Validity.IssuedAtUtc != oldIssued ||
                Validity.ValidUntilUtc != oldValid ||
                Validity.HasPhysicalCopy != oldPhysical ||
                Validity.HasElectronicCopy != oldElectronic)
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        Touch();
        RaiseDomainEvent(new FileValidityChanged(
            Id,
            LastModifiedUtc,
            Validity?.IssuedAtUtc,
            Validity?.ValidUntilUtc,
            Validity?.HasPhysicalCopy ?? false,
            Validity?.HasElectronicCopy ?? false));
        MarkSearchDirty(SearchReindexReason.ValidityChanged);
    }

    /// <summary>
    /// Removes validity information when present.
    /// </summary>
    public void ClearValidity()
    {
        EnsureMutable();

        if (Validity is null)
        {
            return;
        }

        Validity = null;

        Touch();
        RaiseDomainEvent(new FileValidityChanged(Id, LastModifiedUtc, null, null, false, false));
        MarkSearchDirty(SearchReindexReason.ValidityChanged);
    }

    /// <summary>
    /// Creates a search document representation of the file.
    /// </summary>
    /// <param name="extractedText">Optional extracted textual content.</param>
    public SearchDocument ToSearchDocument(string? extractedText = null)
    {
        var validity = Validity;
        return new SearchDocument(
            Id,
            ComposeTitle(),
            Extension.Value,
            Mime.Value,
            Author,
            extractedText,
            CreatedUtc,
            LastModifiedUtc,
            Content.Hash.Value,
            Size.Value,
            validity?.HasPhysicalCopy ?? false,
            validity?.HasElectronicCopy ?? false,
            validity?.IssuedAtUtc,
            validity?.ValidUntilUtc,
            Version);
    }

    /// <summary>
    /// Confirms that the file has been indexed with the provided schema version.
    /// </summary>
    public void ConfirmIndexed(int schemaVersion, DateTimeOffset whenUtc)
    {
        SearchIndex.ConfirmIndexed(schemaVersion, Content.Hash.Value, ComposeTitle(), whenUtc);
    }

    /// <summary>
    /// Bumps the desired search schema version, marking the index as stale when increased.
    /// </summary>
    public void BumpSchemaVersion(int newSchemaVersion)
    {
        if (SearchIndex.BumpSchemaVersion(newSchemaVersion))
        {
            MarkSearchDirty(SearchReindexReason.SchemaUpgrade);
        }
    }

    private static string NormalizeAuthor(string author)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            throw new ArgumentException("Author cannot be null or whitespace.", nameof(author));
        }

        return author.Trim();
    }

    private void EnsureMutable()
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("The file is read-only and cannot be modified.");
        }
    }

    private void Touch()
    {
        LastModifiedUtc = DateTimeOffset.UtcNow;
    }

    private string ComposeTitle() => $"{Name.Value}.{Extension.Value}";

    private void MarkSearchDirty(SearchReindexReason reason)
    {
        SearchIndex.MarkStale();
        RaiseDomainEvent(new SearchReindexRequested(Id, reason, DateTimeOffset.UtcNow));
    }
}
