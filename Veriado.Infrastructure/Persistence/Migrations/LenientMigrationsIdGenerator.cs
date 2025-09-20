using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <summary>
/// Provides a migrations ID generator that tolerates legacy identifiers without timestamps.
/// </summary>
internal sealed class LenientMigrationsIdGenerator : MigrationsIdGenerator
{
    private const char Separator = '_';
    private const int TimestampLength = 14;

    /// <summary>
    /// Initializes a new instance of the <see cref="LenientMigrationsIdGenerator"/> class.
    /// </summary>
    public LenientMigrationsIdGenerator()
    {
    }

    /// <inheritdoc />
    public override string GetName(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return id;
        }

        var separatorIndex = id.IndexOf(Separator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return id;
        }

        if (separatorIndex != TimestampLength)
        {
            return separatorIndex + 1 < id.Length ? id[(separatorIndex + 1)..] : string.Empty;
        }

        return base.GetName(id);
    }
}
