using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <summary>
/// Provides an <see cref="IMigrationsAssembly"/> that tolerates legacy migration identifiers without timestamps.
/// </summary>
internal sealed class LenientMigrationsAssembly : MigrationsAssembly
{
    private const char Separator = '_';

    /// <summary>
    /// Initializes a new instance of the <see cref="LenientMigrationsAssembly"/> class.
    /// </summary>
    /// <param name="currentContext">The current context accessor.</param>
    /// <param name="options">The context options.</param>
    /// <param name="idGenerator">The migrations ID generator.</param>
    /// <param name="logger">The diagnostics logger.</param>
    public LenientMigrationsAssembly(
        ICurrentDbContext currentContext,
        IDbContextOptions options,
        IMigrationsIdGenerator idGenerator,
        IDiagnosticsLogger<DbLoggerCategory.Migrations> logger)
        : base(currentContext, options, idGenerator, logger)
    {
    }

    /// <inheritdoc />
    public override string? FindMigrationId(string nameOrId)
    {
        var migrationId = base.FindMigrationId(nameOrId);
        if (migrationId is not null)
        {
            return migrationId;
        }

        if (string.IsNullOrEmpty(nameOrId))
        {
            return null;
        }

        foreach (var existing in Migrations.Keys)
        {
            if (string.Equals(existing, nameOrId, StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }

            var separatorIndex = existing.IndexOf(Separator);
            if (separatorIndex < 0)
            {
                continue;
            }

            var candidateName = separatorIndex + 1 < existing.Length ? existing[(separatorIndex + 1)..] : string.Empty;
            if (string.Equals(candidateName, nameOrId, StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }
        }

        return null;
    }
}
