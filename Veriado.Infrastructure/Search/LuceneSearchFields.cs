namespace Veriado.Infrastructure.Search;

/// <summary>
/// Defines the field names stored within the Lucene.Net search index.
/// </summary>
internal static class LuceneSearchFields
{
    public const string Id = "id";
    public const string Title = "title";
    public const string Mime = "mime";
    public const string Author = "author";
    public const string Created = "created";
    public const string Modified = "modified";
}
