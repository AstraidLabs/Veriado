using System;

namespace Veriado.Infrastructure.Search;

/// <summary>
/// Represents a failure caused by SQLite reporting that the full-text index is corrupted.
/// </summary>
internal sealed class SearchIndexCorruptedException : Exception
{
    public SearchIndexCorruptedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
