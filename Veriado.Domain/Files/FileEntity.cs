using System;
using System.Collections.Generic;
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
public sealed class FileEntity : AggregateRoot
{
    private const int InitialVersion = 1;

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
        FileContentEntity content,
        UtcTimestamp createdUtc,
        FileSystemMetadata systemMetadata,
        ExtendedMetadata extendedMetadata)
        : base(id)
    {
        Name = name;
        Extension = extension;
        Mime = mime;
        Author = author;
        Content = content;
        Size = content.Length;
        CreatedUtc = createdUtc;
        LastModifiedUtc = createdUtc;
        Version = InitialVersion;
        IsReadOnly = false;
        SystemMetadata = systemMetadata;
        ExtendedMetadata = extendedMetadata;
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
    /// Gets the file size.
    /// </summary>
    public ByteSize Size { get; private set; }

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
    public int Version { get; private set; }

    /// <summary>
    /// Gets the creation timestamp of the file.
    /// </summary>
    public UtcTimestamp CreatedUtc { get; private set; }

    /// <summary>
    /// Gets the last modification timestamp of the file.
    /// </summary>
    public UtcTimestamp LastModifiedUtc { get; private set; }

    /// <summary>
    /// Gets the file content entity.
    /// </summary>
    public FileContentEntity Content { get; private set; } = null!;

    /// <summary>
    /// Gets the optional document validity information.
    /// </summary>
    public FileDocumentValidityEntity? Validity { get; private set; }

    /// <summary>
    /// Gets the latest file system metadata snapshot.
    /// </summary>
    public FileSystemMetadata SystemMetadata { get; private set; }

    /// <summary>
    /// Gets the extended metadata collection.
    /// </summary>
    public ExtendedMetadata ExtendedMetadata { get; private set; } = ExtendedMetadata.Empty;

    /// <summary>
    /// Gets the search index state.
    /// </summary>
    public SearchIndexState SearchIndex { get; private set; } = null!;

    /// <summary>
    /// Gets the FTS5 tokenizer policy.
    /// </summary>
    public Fts5Policy FtsPolicy { get; private set; } = null!;

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
        byte[] bytes,
        UtcTimestamp createdUtc,
        int? maxContentSize = null)
    {
        var normalizedAuthor = NormalizeAuthor(author);
        var content = FileContentEntity.FromBytes(bytes, maxContentSize);
        var systemMetadata = new FileSystemMetadata(FileAttributesFlags.Normal, createdUtc, createdUtc, createdUtc, null, null, null);
        var metadataBuilder = ExtendedMetadata.Empty.ToBuilder();
        metadataBuilder.Set(WindowsPropertyIds.Author, MetadataValue.FromString(normalizedAuthor));
        var extendedMetadata = metadataBuilder.Build();

        var entity = new FileEntity(Guid.NewGuid(), name, extension, mime, normalizedAuthor, content, createdUtc, systemMetadata, extendedMetadata);
        entity.RaiseDomainEvent(new FileCreated(entity.Id, entity.Name, entity.Extension, entity.Mime, entity.Author, entity.Size, entity.Content.Hash, createdUtc));
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
        RaiseDomainEvent(new FileMetadataUpdated(Id, Mime, Author, SystemMetadata, whenUtc));
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

            if (SetMetadataStringInternal(WindowsPropertyIds.Author, normalized))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        Touch(whenUtc);
        RaiseDomainEvent(new FileMetadataUpdated(Id, Mime, Author, SystemMetadata, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.MetadataChanged);
    }

    /// <summary>
    /// Replaces the file content and bumps the version if the hash changes.
    /// </summary>
    /// <param name="bytes">The new content bytes.</param>
    /// <param name="maxContentSize">Optional maximum content length.</param>
    public void ReplaceContent(byte[] bytes, UtcTimestamp whenUtc, int? maxContentSize = null)
    {
        EnsureWritable();
        var newContent = FileContentEntity.FromBytes(bytes, maxContentSize);
        if (newContent.Hash == Content.Hash)
        {
            return;
        }

        Content = newContent;
        Size = newContent.Length;
        BumpVersion();
        Touch(whenUtc);
        RaiseDomainEvent(new FileContentReplaced(Id, newContent.Hash, newContent.Length, Version, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.ContentChanged);
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
        RaiseDomainEvent(new FileMetadataUpdated(Id, Mime, Author, SystemMetadata, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.MetadataChanged);
    }

    /// <summary>
    /// Mutates the extended metadata using a builder callback.
    /// </summary>
    /// <param name="configure">The builder configuration action.</param>
    public void SetExtendedMetadata(UtcTimestamp whenUtc, Action<ExtendedMetadata.Builder> configure)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(configure);

        var builder = ExtendedMetadata.ToBuilder();
        configure(builder);
        var updated = builder.Build();
        if (ExtendedMetadata.Equals(updated))
        {
            return;
        }

        ExtendedMetadata = updated;
        AlignAuthorWithMetadata();
        Touch(whenUtc);
        RaiseDomainEvent(new FileMetadataUpdated(Id, Mime, Author, SystemMetadata, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.MetadataChanged);
    }

    /// <summary>
    /// Gets the document title from extended metadata if available.
    /// </summary>
    /// <returns>The stored title or <see langword="null"/>.</returns>
    public string? GetTitle() => GetMetadataString(WindowsPropertyIds.Title);

    /// <summary>
    /// Sets the document title and marks the search index for reindexing.
    /// </summary>
    /// <param name="title">The new title value.</param>
    public void SetTitle(string? title, UtcTimestamp whenUtc) => SetMetadataString(WindowsPropertyIds.Title, title, whenUtc);

    /// <summary>
    /// Gets the document subject from extended metadata if available.
    /// </summary>
    /// <returns>The stored subject or <see langword="null"/>.</returns>
    public string? GetSubject() => GetMetadataString(WindowsPropertyIds.Subject);

    /// <summary>
    /// Sets the document subject and marks the search index for reindexing.
    /// </summary>
    /// <param name="subject">The new subject value.</param>
    public void SetSubject(string? subject, UtcTimestamp whenUtc) => SetMetadataString(WindowsPropertyIds.Subject, subject, whenUtc);

    /// <summary>
    /// Gets the company metadata value if available.
    /// </summary>
    /// <returns>The stored company or <see langword="null"/>.</returns>
    public string? GetCompany() => GetMetadataString(WindowsPropertyIds.Company);

    /// <summary>
    /// Sets the company metadata value.
    /// </summary>
    /// <param name="company">The new company value.</param>
    public void SetCompany(string? company, UtcTimestamp whenUtc) => SetMetadataString(WindowsPropertyIds.Company, company, whenUtc);

    /// <summary>
    /// Gets the manager metadata value if available.
    /// </summary>
    /// <returns>The stored manager or <see langword="null"/>.</returns>
    public string? GetManager() => GetMetadataString(WindowsPropertyIds.Manager);

    /// <summary>
    /// Sets the manager metadata value.
    /// </summary>
    /// <param name="manager">The new manager value.</param>
    public void SetManager(string? manager, UtcTimestamp whenUtc) => SetMetadataString(WindowsPropertyIds.Manager, manager, whenUtc);

    /// <summary>
    /// Gets the comments metadata value if available.
    /// </summary>
    /// <returns>The stored comments or <see langword="null"/>.</returns>
    public string? GetComments() => GetMetadataString(WindowsPropertyIds.Comments);

    /// <summary>
    /// Sets the comments metadata value.
    /// </summary>
    /// <param name="comments">The new comments value.</param>
    public void SetComments(string? comments, UtcTimestamp whenUtc) => SetMetadataString(WindowsPropertyIds.Comments, comments, whenUtc);

    /// <summary>
    /// Gets the author metadata value, preferring extended metadata over the core value.
    /// </summary>
    /// <returns>The author metadata value.</returns>
    public string GetAuthor()
    {
        var metadataAuthor = GetMetadataString(WindowsPropertyIds.Author);
        return metadataAuthor ?? Author;
    }

    /// <summary>
    /// Sets the author metadata value, synchronizing the core property and extended metadata.
    /// </summary>
    /// <param name="author">The new author value.</param>
    public void SetAuthor(string author, UtcTimestamp whenUtc)
    {
        EnsureWritable();
        var normalized = NormalizeAuthor(author);
        var changed = false;

        if (!string.Equals(Author, normalized, StringComparison.Ordinal))
        {
            Author = normalized;
            changed = true;
        }

        if (SetMetadataStringInternal(WindowsPropertyIds.Author, normalized))
        {
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        Touch(whenUtc);
        RaiseDomainEvent(new FileMetadataUpdated(Id, Mime, Author, SystemMetadata, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.MetadataChanged);
    }

    /// <summary>
    /// Gets the last author metadata value if available.
    /// </summary>
    /// <returns>The stored last author or <see langword="null"/>.</returns>
    public string? GetLastAuthor() => GetMetadataString(WindowsPropertyIds.LastAuthor);

    /// <summary>
    /// Sets the last author metadata value.
    /// </summary>
    /// <param name="lastAuthor">The new last author value.</param>
    public void SetLastAuthor(string? lastAuthor, UtcTimestamp whenUtc) => SetMetadataString(WindowsPropertyIds.LastAuthor, lastAuthor, whenUtc);

    /// <summary>
    /// Gets the category metadata value if available.
    /// </summary>
    /// <returns>The stored category or <see langword="null"/>.</returns>
    public string? GetCategory() => GetMetadataString(WindowsPropertyIds.Category);

    /// <summary>
    /// Sets the category metadata value.
    /// </summary>
    /// <param name="category">The new category value.</param>
    public void SetCategory(string? category, UtcTimestamp whenUtc) => SetMetadataString(WindowsPropertyIds.Category, category, whenUtc);

    /// <summary>
    /// Gets the template metadata value if available.
    /// </summary>
    /// <returns>The stored template or <see langword="null"/>.</returns>
    public string? GetTemplate() => GetMetadataString(WindowsPropertyIds.Template);

    /// <summary>
    /// Sets the template metadata value.
    /// </summary>
    /// <param name="template">The new template value.</param>
    public void SetTemplate(string? template, UtcTimestamp whenUtc) => SetMetadataString(WindowsPropertyIds.Template, template, whenUtc);

    /// <summary>
    /// Gets the revision number metadata value if available.
    /// </summary>
    /// <returns>The stored revision number or <see langword="null"/>.</returns>
    public string? GetRevisionNumber() => GetMetadataString(WindowsPropertyIds.RevisionNumber);

    /// <summary>
    /// Sets the revision number metadata value.
    /// </summary>
    /// <param name="revision">The new revision number value.</param>
    public void SetRevisionNumber(string? revision, UtcTimestamp whenUtc) => SetMetadataString(WindowsPropertyIds.RevisionNumber, revision, whenUtc);

    /// <summary>
    /// Builds a search document representation of the file for full-text indexing.
    /// </summary>
    /// <param name="extractedText">Optional extracted body text.</param>
    /// <returns>The search document.</returns>
    public SearchDocument ToSearchDocument(string? extractedText = null)
    {
        var title = GetTitle() ?? Name.Value;
        var authorText = string.IsNullOrWhiteSpace(Author) ? null : Author;
        var subject = GetSubject();
        var comments = GetComments();

        var contentParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(extractedText))
        {
            contentParts.Add(extractedText!);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            contentParts.Add(title);
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            contentParts.Add(subject!);
        }

        if (!string.IsNullOrWhiteSpace(comments))
        {
            contentParts.Add(comments!);
        }

        if (!string.IsNullOrWhiteSpace(authorText))
        {
            contentParts.Add(authorText!);
        }

        var contentText = contentParts.Count == 0 ? null : string.Join(Environment.NewLine, contentParts);
        return new SearchDocument(
            Id,
            title,
            Mime.Value,
            authorText,
            subject,
            CreatedUtc.Value,
            LastModifiedUtc.Value,
            contentText);
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
    public void ConfirmIndexed(int schemaVersion, UtcTimestamp whenUtc)
    {
        SearchIndex.ApplyIndexed(schemaVersion, whenUtc.Value, Content.Hash.Value, GetTitle());
    }

    /// <summary>
    /// Marks the search state as requiring an upgrade to a new schema version.
    /// </summary>
    /// <param name="newSchemaVersion">The new schema version.</param>
    public void BumpSchemaVersion(int newSchemaVersion, UtcTimestamp whenUtc)
    {
        if (newSchemaVersion <= SearchIndex.SchemaVersion)
        {
            return;
        }

        SearchIndex = new SearchIndexState(
            newSchemaVersion,
            SearchIndex.IsStale,
            SearchIndex.LastIndexedUtc,
            SearchIndex.IndexedContentHash,
            SearchIndex.IndexedTitle);
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

    private static string? NormalizeOptionalMetadata(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private string? GetMetadataString(PropertyKey key)
    {
        return ExtendedMetadata.TryGet(key, out var metadata) && metadata.TryGetString(out var value)
            ? value
            : null;
    }

    private void SetMetadataString(PropertyKey key, string? value, UtcTimestamp whenUtc)
    {
        EnsureWritable();
        var normalized = NormalizeOptionalMetadata(value);
        if (!SetMetadataStringInternal(key, normalized))
        {
            return;
        }

        if (key == WindowsPropertyIds.Author)
        {
            AlignAuthorWithMetadata();
        }

        Touch(whenUtc);
        RaiseDomainEvent(new FileMetadataUpdated(Id, Mime, Author, SystemMetadata, whenUtc));
        MarkSearchDirty(whenUtc, ReindexReason.MetadataChanged);
    }

    private bool SetMetadataStringInternal(PropertyKey key, string? value)
    {
        return ApplyExtendedMetadataMutation(builder =>
        {
            if (value is null)
            {
                builder.Remove(key);
            }
            else
            {
                builder.Set(key, MetadataValue.FromString(value));
            }
        });
    }

    internal void LoadExtendedMetadata(ExtendedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ExtendedMetadata = metadata;
    }

    private bool ApplyExtendedMetadataMutation(Action<ExtendedMetadata.Builder> configure)
    {
        var builder = ExtendedMetadata.ToBuilder();
        configure(builder);
        var updated = builder.Build();
        if (ExtendedMetadata.Equals(updated))
        {
            return false;
        }

        ExtendedMetadata = updated;
        return true;
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

    private void BumpVersion()
    {
        if (Version == int.MaxValue)
        {
            throw new InvalidOperationException("File version overflow.");
        }

        Version += 1;
    }

    private void MarkSearchDirty(UtcTimestamp whenUtc, ReindexReason reason)
    {
        SearchIndex.MarkStale();
        RaiseDomainEvent(new SearchReindexRequested(Id, reason, whenUtc));
    }

    private void AlignAuthorWithMetadata()
    {
        if (ExtendedMetadata.TryGet(WindowsPropertyIds.Author, out var metadata) && metadata.TryGetString(out var value))
        {
            var normalized = NormalizeAuthor(value);
            Author = normalized;
        }
        else
        {
            SetMetadataStringInternal(WindowsPropertyIds.Author, Author);
        }
    }
}
