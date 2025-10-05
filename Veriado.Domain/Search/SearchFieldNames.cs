namespace Veriado.Domain.Search;

/// <summary>
/// Provides canonical field names exposed by the Lucene search index.
/// </summary>
public static class SearchFieldNames
{
    public const string Id = "id";
    public const string Title = "title";
    public const string Author = "author";
    public const string MetadataText = "metadata_text";
    public const string MetadataTextStored = "metadata_text_store";
    public const string Metadata = "metadata";
    public const string Mime = "mime";
    public const string MimeSearch = "mime_search";
    public const string FileName = "filename";
    public const string FileNameSearch = "filename_search";
    public const string ContentHash = "content_hash";
    public const string CreatedUtc = "created_utc";
    public const string ModifiedUtc = "modified_utc";
    public const string CreatedTicks = "created_ticks";
    public const string ModifiedTicks = "modified_ticks";
    public const string ModifiedTicksSort = "modified_ticks_sort";
    public const string SizeBytes = "size_bytes";
    public const string SizeBytesSort = "size_bytes_sort";
    public const string CatchAll = "catch_all";
}
