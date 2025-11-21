// File: Veriado.Contracts/Storage/StorageMigrationResultDto.cs
namespace Veriado.Contracts.Storage;

/// <summary>
/// Represents the outcome of a storage root migration operation.
/// </summary>
public sealed record StorageMigrationResultDto(
    string OldRoot,
    string NewRoot,
    int MigratedFiles,
    int MissingSources,
    int VerificationFailures,
    IReadOnlyCollection<string> Errors);
