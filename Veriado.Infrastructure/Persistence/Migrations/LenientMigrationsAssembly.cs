using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure.Persistence.Migrations;

/// <summary>
/// Provides a lenient <see cref="IMigrationsAssembly"/> implementation that tolerates migration
/// identifier collisions by generating alternative identifiers instead of throwing.
/// </summary>
public sealed class LenientMigrationsAssembly : IMigrationsAssembly
{
    private readonly Type _contextType;
    private readonly IDiagnosticsLogger<DbLoggerCategory.Migrations> _logger;
    private readonly IMigrationsIdGenerator _idGenerator;
    private readonly LenientMigrationsIdGenerator? _lenientGenerator;

    private IReadOnlyDictionary<string, TypeInfo>? _migrations;
    private ModelSnapshot? _modelSnapshot;

    public LenientMigrationsAssembly(
        ICurrentDbContext currentContext,
        IDbContextOptions options,
        IMigrationsIdGenerator idGenerator,
        IDiagnosticsLogger<DbLoggerCategory.Migrations> logger)
    {
        _contextType = currentContext.Context.GetType();
        _logger = logger;
        _idGenerator = idGenerator;
        _lenientGenerator = idGenerator as LenientMigrationsIdGenerator;

        var optionsExtension = RelationalOptionsExtension.Extract(options);
        var assemblyName = optionsExtension.MigrationsAssembly;
        var assemblyObject = optionsExtension.MigrationsAssemblyObject;

        Assembly = assemblyName == null
            ? assemblyObject ?? _contextType.Assembly
            : Assembly.Load(new AssemblyName(assemblyName));
    }

    /// <inheritdoc />
    public Assembly Assembly { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, TypeInfo> Migrations
        => _migrations ??= CreateMigrations();

    /// <inheritdoc />
    public ModelSnapshot? ModelSnapshot
        => _modelSnapshot ??= CreateModelSnapshot();

    /// <inheritdoc />
    public string? FindMigrationId(string nameOrId)
    {
        if (nameOrId == null)
        {
            return null;
        }

        if (_idGenerator.IsValidId(nameOrId))
        {
            return Migrations.Keys
                .FirstOrDefault(id => string.Equals(id, nameOrId, StringComparison.OrdinalIgnoreCase));
        }

        return Migrations.Keys
            .FirstOrDefault(id => string.Equals(_idGenerator.GetName(id), nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
    {
        var migration = (Migration)Activator.CreateInstance(migrationClass.AsType())!;
        migration.ActiveProvider = activeProvider;
        return migration;
    }

    private IReadOnlyDictionary<string, TypeInfo> CreateMigrations()
    {
        var result = new SortedDictionary<string, TypeInfo>();

        IEnumerable<(string? Id, TypeInfo Type)> Query()
            => from type in Assembly.GetConstructibleTypes()
               where type.IsSubclassOf(typeof(Migration))
                     && type.GetCustomAttribute<DbContextAttribute>()?.ContextType == _contextType
               let id = type.GetCustomAttribute<MigrationAttribute>()?.Id
               orderby id
               select (id, type);

        foreach (var (id, type) in Query())
        {
            if (id is null)
            {
                _logger.MigrationAttributeMissingWarning(type);
                continue;
            }

            if (result.TryAdd(id, type))
            {
                continue;
            }

            var replacement = ResolveDuplicateIdentifier(result, id, type);
            result[replacement] = type;
        }

        return new ReadOnlyDictionary<string, TypeInfo>(result);
    }

    private ModelSnapshot? CreateModelSnapshot()
        => (from type in Assembly.GetConstructibleTypes()
                where type.IsSubclassOf(typeof(ModelSnapshot))
                    && type.GetCustomAttribute<DbContextAttribute>()?.ContextType == _contextType
                select (ModelSnapshot)Activator.CreateInstance(type.AsType())!)
            .FirstOrDefault();

    private string ResolveDuplicateIdentifier(
        IDictionary<string, TypeInfo> migrations,
        string duplicateId,
        TypeInfo migrationType)
    {
        var baseName = _idGenerator.IsValidId(duplicateId)
            ? _idGenerator.GetName(duplicateId)
            : duplicateId;
        var timestampPrefix = ExtractTimestampPrefix(duplicateId);
        var attempt = 2;

        while (true)
        {
            var candidateName = $"{baseName}_{attempt++}";
            var candidateId = timestampPrefix is null
                ? _idGenerator.GenerateId(candidateName)
                : $"{timestampPrefix}_{candidateName}";

            if (migrations.ContainsKey(candidateId))
            {
                continue;
            }

            if (_lenientGenerator is not null)
            {
                candidateId = _lenientGenerator.EnsureUniqueId(candidateId, id => !migrations.ContainsKey(id));
            }

            LogDuplicateResolution(duplicateId, migrationType, candidateId, migrations[duplicateId]);
            return candidateId;
        }
    }

    private void LogDuplicateResolution(
        string duplicateId,
        TypeInfo newMigration,
        string resolvedId,
        TypeInfo existingMigration)
    {
        _logger.Logger.LogWarning(
            "Duplicate migration identifier '{MigrationId}' detected for migration '{DuplicateMigration}'. " +
            "Keeping '{ExistingMigration}' with the original identifier and using '{ResolvedId}' for the duplicate.",
            duplicateId,
            newMigration.FullName,
            existingMigration.FullName,
            resolvedId);
    }

    private static string? ExtractTimestampPrefix(string id)
    {
        var separatorIndex = id.IndexOf('_');
        if (separatorIndex <= 0)
        {
            return null;
        }

        for (var i = 0; i < separatorIndex; i++)
        {
            if (!char.IsDigit(id[i]))
            {
                return null;
            }
        }

        return id[..separatorIndex];
    }
}
