namespace Veriado.Infrastructure.Search;

using System;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Provides compatibility shims for SQLite database functions removed from EF Core 9.
/// </summary>
internal static class SqliteDbFunctionsExtensionsCompat
{
    /// <summary>
    /// Maps to the SQLite <c>strftime</c> function to support formatting timestamps in LINQ queries.
    /// </summary>
    /// <remarks>
    /// The method is intended for use inside LINQ-to-Entities expressions only.
    /// </remarks>
    /// <param name="_">The EF Core database functions entry point.</param>
    /// <param name="format">The <c>strftime</c> format string.</param>
    /// <param name="timestring">The ISO 8601 timestamp value.</param>
    /// <returns>The formatted timestamp value.</returns>
    [DbFunction("strftime", IsBuiltIn = true)]
    public static string? Strftime(this DbFunctions _, string format, string timestring)
        => throw new InvalidOperationException("This method is only intended for use in database queries.");
}
