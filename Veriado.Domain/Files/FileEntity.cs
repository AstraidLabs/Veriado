using Veriado.Domain.Files.Events;
using Veriado.Domain.Metadata;
using Veriado.Domain.Primitives;
using Veriado.Domain.Search;
using Veriado.Domain.Search.Events;
using Veriado.Domain.ValueObjects;

namespace Veriado.Domain.Files;

/// <summary>
/// Represents a rich aggregate root for managing file metadata, content and full-text indexing state.
/// </summary>
public sealed partial class FileEntity : AggregateRoot
{
    private FileEntity(Guid id)
        : base(id)
    {
    }

    private FileEntity(
        Guid id,
        FileName name,
        FileExtension extension,
        MimeType mime,
        string author,
        Guid fileSystemId,
        FileContentLink content,
        UtcTimestamp createdUtc,
        FileSystemMetadata systemMetadata,
        string? title)
        : base(id)
    {
        Name = name;
        Extension = extension;
        Mime = mime;
        Author = author;
        FileSystemId = fileSystemId;
        Content = content;
        CreatedUtc = createdUtc;
        LastModifiedUtc = createdUtc;
        ContentRevision = content.Version.Value;
        IsReadOnly = false;
        SystemMetadata = systemMetadata;
        Title = NormalizeOptionalText(title);
        SearchIndex = new SearchIndexState(schemaVersion: 1);
        FtsPolicy = Fts5Policy.Default;
    }

    /// <summary>
    /// Gets the file name without extension.
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
    /// Gets the snapshot describing the linked content.
    /// </summary>
    public FileContentLink? Content { get; private set; }

    /// <summary>
    /// Gets the file size.
    /// </summary>
    public ByteSize Size => Content?.Size ?? default;

    /// <summary>
    /// Gets the file author stored in the core metadata.
    /// </summary>
    public string Author { get; private set; } = null!;

    /// <summary>
    /// Gets a value indicating whether the file is read-only.
    /// </summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// Gets the version number of the file content.
    /// </summary>
    public int ContentRevision { get; private set; }

    /// <summary>
    /// Gets the creation timestamp of the file.
    /// </summary>
    public UtcTimestamp CreatedUtc { get; private set; }

    /// <summary>
    /// Gets the last modification timestamp of the file.
    /// </summary>
    public UtcTimestamp LastModifiedUtc { get; private set; }

    /// <summary>
    /// Gets the identifier of the linked file system content.
    /// </summary>
    public Guid FileSystemId { get; private set; }

    /// <summary>
    /// Gets the version of the currently linked content.
    /// </summary>
    public ContentVersion LinkedContentVersion => Content?.Version ?? throw new InvalidOperationException("File content has not been linked.");

    /// <summary>
    /// Gets the SHA-256 hash of the linked content.
    /// </summary>
    public FileHash ContentHash => Content?.ContentHash ?? throw new InvalidOperationException("File content has not been linked.");

    /// <summary>
    /// Gets the optional document validity information.
    /// </summary>
    public FileDocumentValidityEntity? Validity { get; private set; }

    /// <summary>
    /// Gets the latest file system metadata snapshot.
    /// </summary>
    public FileSystemMetadata SystemMetadata { get; private set; }

    /// <summary>
    /// Gets the optional display title for the document.
    /// </summary>
    public string? Title { get; private set; }

    /// <summary>
    /// Gets the search index state.
    /// </summary>
    public SearchIndexState SearchIndex { get; private set; } = new SearchIndexState(schemaVersion: 1);

    /// <summary>
    /// Gets the FTS5 tokenizer policy.
    /// </summary>
    public Fts5Policy FtsPolicy { get; private set; } = Fts5Policy.Default;

    /// <summary>
    /// Creates a new file aggregate from the provided core information and binary content.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="extension">The file extension.</param>
    /// <param name="mime">The MIME type.</param>
    /// <param name="author">The document author.</param>
    /// <param name="bytes">The file content bytes.</param>
    /// <param name="maxContentSize">Optional maximum content length.</param>
    /// <returns>The created aggregate root.</returns>
    public static FileEntity CreateNew(
        FileName name,
        FileExtension extension,
        MimeType mime,
        string author,
        Guid fileSystemId,
        string contentProvider,
        string contentLocation,
        FileHash contentHash,
        ByteSize size,
        ContentVersion version,
        UtcTimestamp createdUtc,
        string? title = null,
        FileSystemMetadata? systemMetadata = null)
    {
        var normalizedAuthor = NormalizeAuthor(author);
        var metadata = systemMetadata ?? new FileSystemMetadata(
            FileAttributesFlags.Normal,
            createdUtc,
            createdUtc,
            createdUtc,
            ownerSid: null,
            hardLinkCount: null,
            alternateDataStreamCount: null);

        var content = FileContentLink.Create(
            contentProvider,
            contentLocation,
            contentHash,
            size,
            version,
            createdUtc,
            mime);

        var entity = new FileEntity(
            Guid.NewGuid(),
            name,
            extension,
            mime,
            normalizedAuthor,
            fileSystemId,
            content,
            createdUtc,
            metadata,
            title);
        entity.RaiseDomainEvent(new FileCreated(entity.Id, entity.Name, entity.Extension, entity.Mime, entity.Author, entity.Size, entity.ContentHash, createdUtc));
        entity.RaiseDomainEvent(new FileContentLinked(entity.Id, entity.FileSystemId, entity.Content!, createdUtc));
        entity.MarkSearchDirty(createdUtc, ReindexReason.Created);
        return entity;
    }

    /// <summary>
    /// Renames the file, emitting metadata and search reindex events.
    /// </summary>
    /// <param name="newName">The new file name.</param>
    public void Rename(FileName newName, UtcTimestamp whenUtc)
    {
        EnsureWritable();
        if (newName == Name)
        {
            return;
        }

        var previous = Name;
        Name = newName;
        Touch(whenUtc);
        RaiseDomainEvent(new FileRenamed(Id, previous, newName, whenUtc));
        RaiseDomainEvent(new FileMetadataUpdated(Id, Mime, Author, Title, SystemMetadata, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.MetadataChanged);
    }

    /// <summary>
    /// Updates core descriptive metadata such as MIME type and author.
    /// </summary>
    /// <param name="mime">The optional new MIME type.</param>
    /// <param name="author">The optional new author.</param>
    public void UpdateMetadata(MimeType? mime, string? author, UtcTimestamp whenUtc)
    {
        EnsureWritable();
        var changed = false;

        if (mime.HasValue && mime.Value != Mime)
        {
            Mime = mime.Value;
            changed = true;
        }

        if (author is not null)
        {
            var normalized = NormalizeAuthor(author);
            if (!string.Equals(Author, normalized, StringComparison.Ordinal))
            {
                Author = normalized;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        Touch(whenUtc);
        RaiseDomainEvent(new FileMetadataUpdated(Id, Mime, Author, Title, SystemMetadata, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.MetadataChanged);
    }

    /// <summary>
    /// Links the file to newly created physical content.
    /// </summary>
    /// <param name="link">The immutable snapshot describing the link.</param>
    /// <param name="clock">The clock used to capture the event timestamp.</param>
    public void LinkNewContent(FileContentLink link, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureWritable();

        if (Content is not null && link.Version.Value <= Content.Version.Value)
        {
            throw new InvalidOperationException("New content link must increase the content version.");
        }

        var previousHash = Content?.ContentHash;
        var occurredAt = UtcTimestamp.From(clock.UtcNow);

        Content = link;
        ContentRevision = link.Version.Value;
        if (link.Mime is MimeType mime && Mime != mime)
        {
            Mime = mime;
        }
        Touch(occurredAt);

        RaiseDomainEvent(new FileContentLinked(Id, FileSystemId, link, occurredAt));

        if (previousHash is null || previousHash != link.ContentHash)
        {
            MarkSearchDirty(occurredAt, ReindexReason.ContentChanged);
        }
    }

    /// <summary>
    /// Relinks the file to an existing content snapshot without changing the version.
    /// </summary>
    /// <param name="link">The snapshot of the content link to reuse.</param>
    /// <param name="clock">The clock used to capture the event timestamp.</param>
    public void RelinkToExistingContent(FileContentLink link, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureWritable();

        if (Content is not null && link.Version.Value < Content.Version.Value)
        {
            throw new InvalidOperationException("Cannot relink to content with a lower version.");
        }

        var previousHash = Content?.ContentHash;
        var occurredAt = UtcTimestamp.From(clock.UtcNow);

        Content = link;
        ContentRevision = link.Version.Value;
        if (link.Mime is MimeType mime && Mime != mime)
        {
            Mime = mime;
        }
        Touch(occurredAt);

        RaiseDomainEvent(new FileContentRelinked(Id, FileSystemId, link, occurredAt));

        if (previousHash is null || previousHash != link.ContentHash)
        {
            MarkSearchDirty(occurredAt, ReindexReason.ContentChanged);
        }
    }

    /// <summary>
    /// Sets the read-only flag for the file.
    /// </summary>
    /// <param name="isReadOnly">The new read-only state.</param>
    public void SetReadOnly(bool isReadOnly, UtcTimestamp whenUtc)
    {
        if (IsReadOnly == isReadOnly)
        {
            return;
        }

        IsReadOnly = isReadOnly;
        Touch(whenUtc);
        RaiseDomainEvent(new FileReadOnlyChanged(Id, IsReadOnly, whenUtc));
    }

    /// <summary>
    /// Sets or updates the document validity information.
    /// </summary>
    /// <param name="issuedAt">The issue timestamp.</param>
    /// <param name="validUntil">The expiration timestamp.</param>
    /// <param name="hasPhysicalCopy">Whether a physical copy exists.</param>
    /// <param name="hasElectronicCopy">Whether an electronic copy exists.</param>
    public void SetValidity(UtcTimestamp issuedAt, UtcTimestamp validUntil, bool hasPhysicalCopy, bool hasElectronicCopy, UtcTimestamp whenUtc)
    {
        EnsureWritable();
        var changed = false;
        if (Validity is null)
        {
            Validity = new FileDocumentValidityEntity(issuedAt, validUntil, hasPhysicalCopy, hasElectronicCopy);
            changed = true;
        }
        else
        {
            var previousIssued = Validity.IssuedAt;
            var previousValid = Validity.ValidUntil;
            var previousPhysical = Validity.HasPhysicalCopy;
            var previousElectronic = Validity.HasElectronicCopy;

            Validity.SetPeriod(issuedAt, validUntil);
            Validity.SetCopies(hasPhysicalCopy, hasElectronicCopy);

            changed = previousIssued != Validity.IssuedAt
                || previousValid != Validity.ValidUntil
                || previousPhysical != Validity.HasPhysicalCopy
                || previousElectronic != Validity.HasElectronicCopy;
        }

        if (!changed)
        {
            return;
        }

        Touch(whenUtc);
        RaiseDomainEvent(new FileValidityChanged(Id, Validity?.IssuedAt, Validity?.ValidUntil, Validity?.HasPhysicalCopy ?? false, Validity?.HasElectronicCopy ?? false, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.ValidityChanged);
    }

    /// <summary>
    /// Clears the document validity information if present.
    /// </summary>
    public void ClearValidity(UtcTimestamp whenUtc)
    {
        EnsureWritable();
        if (Validity is null)
        {
            return;
        }

        Validity = null;
        Touch(whenUtc);
        RaiseDomainEvent(new FileValidityChanged(Id, null, null, false, false, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.ValidityChanged);
    }

    /// <summary>
    /// Applies a file system metadata snapshot to the aggregate.
    /// </summary>
    /// <param name="metadata">The new system metadata snapshot.</param>
    public void ApplySystemMetadata(FileSystemMetadata metadata, UtcTimestamp whenUtc)
    {
        EnsureWritable();
        if (SystemMetadata == metadata)
        {
            return;
        }

        SystemMetadata = metadata;
        Touch(whenUtc);
        RaiseDomainEvent(new FileMetadataUpdated(Id, Mime, Author, Title, SystemMetadata, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.MetadataChanged);
    }

    /// <summary>
    /// Updates the optional document title used for display and search.
    /// </summary>
    /// <param name="title">The new title value.</param>
    public void SetTitle(string? title, UtcTimestamp whenUtc)
    {
        EnsureWritable();
        var normalized = NormalizeOptionalText(title);
        if (Title == normalized)
        {
            return;
        }

        Title = normalized;
        Touch(whenUtc);
        RaiseDomainEvent(new FileMetadataUpdated(Id, Mime, Author, Title, SystemMetadata, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.MetadataChanged);
    }

    /// <summary>
    /// Sets the document author and marks the metadata as updated.
    /// </summary>
    /// <param name="author">The new author value.</param>
    public void SetAuthor(string author, UtcTimestamp whenUtc)
    {
        EnsureWritable();
        var normalized = NormalizeAuthor(author);
        if (string.Equals(Author, normalized, StringComparison.Ordinal))
        {
            return;
        }

        Author = normalized;
        Touch(whenUtc);
        RaiseDomainEvent(new FileMetadataUpdated(Id, Mime, Author, Title, SystemMetadata, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.MetadataChanged);
    }

    /// <summary>
    /// Builds a search document representation of the file for full-text indexing.
    /// </summary>
    /// <returns>The search document.</returns>
    public SearchDocument ToSearchDocument()
    {
        var title = string.IsNullOrWhiteSpace(Title) ? Name.Value : Title!;
        var authorText = string.IsNullOrWhiteSpace(Author) ? null : Author;
        var metadata = new SearchDocumentMetadata(
            Name.Value,
            Extension.Value,
            Mime.Value,
            string.IsNullOrWhiteSpace(Title) ? null : Title,
            authorText,
            Size.Value,
            new SearchDocumentSystemMetadata(
                SystemMetadata.Attributes,
                SystemMetadata.OwnerSid,
                SystemMetadata.HardLinkCount,
                SystemMetadata.AlternateDataStreamCount,
                SystemMetadata.CreatedUtc.Value,
                SystemMetadata.LastWriteUtc.Value,
                SystemMetadata.LastAccessUtc.Value));
        var metadataJson = SearchDocument.SerializeMetadata(metadata);
        var metadataText = SearchDocument.BuildMetadataText(metadata);
        return new SearchDocument(
            Id,
            title,
            Mime.Value,
            authorText,
            Name.Value,
            CreatedUtc.Value,
            LastModifiedUtc.Value,
            metadataJson,
            metadataText,
            ContentHash.Value);
    }

    /// <summary>
    /// Requests a manual rebuild of the search index entry for this file.
    /// </summary>
    public void RequestManualReindex(UtcTimestamp whenUtc)
    {
        MarkSearchDirty(whenUtc, ReindexReason.Manual);
    }

    /// <summary>
    /// Confirms that the file has been indexed with the specified schema version and timestamp.
    /// </summary>
    /// <param name="schemaVersion">The applied schema version.</param>
    /// <param name="whenUtc">The time of indexing.</param>
    public void ConfirmIndexed(
        int schemaVersion,
        UtcTimestamp whenUtc,
        string analyzerVersion,
        string? tokenHash,
        string? indexedTitle = null)
    {
        SearchIndex ??= new SearchIndexState(schemaVersion: 1);
        var resolvedTitle = string.IsNullOrWhiteSpace(indexedTitle)
            ? string.IsNullOrWhiteSpace(Title)
                ? Name.Value
                : Title!
            : indexedTitle!;
        SearchIndex.ApplyIndexed(schemaVersion, whenUtc.Value, ContentHash.Value, resolvedTitle, analyzerVersion, tokenHash);
    }

    /// <summary>
    /// Marks the search state as requiring an upgrade to a new schema version.
    /// </summary>
    /// <param name="newSchemaVersion">The new schema version.</param>
    public void BumpSchemaVersion(int newSchemaVersion, UtcTimestamp whenUtc)
    {
        SearchIndex ??= new SearchIndexState(schemaVersion: 1);
        var current = SearchIndex;

        if (newSchemaVersion <= current.SchemaVersion)
        {
            return;
        }

        SearchIndex = new SearchIndexState(
            newSchemaVersion,
            current.IsStale,
            current.LastIndexedUtc,
            current.IndexedContentHash,
            current.IndexedTitle,
            current.AnalyzerVersion,
            current.TokenHash);
        MarkSearchDirty(whenUtc, ReindexReason.SchemaUpgrade);
    }

    private static string NormalizeAuthor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Author cannot be null or whitespace.", nameof(value));
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private void EnsureWritable()
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("The file is read-only and cannot be modified.");
        }
    }

    private void Touch(UtcTimestamp whenUtc)
    {
        LastModifiedUtc = whenUtc;
    }

    private void MarkSearchDirty(UtcTimestamp whenUtc, ReindexReason reason)
    {
        SearchIndex ??= new SearchIndexState(schemaVersion: 1);
        SearchIndex.MarkStale();
        RaiseDomainEvent(new SearchReindexRequested(Id, reason, whenUtc));
    }
}
