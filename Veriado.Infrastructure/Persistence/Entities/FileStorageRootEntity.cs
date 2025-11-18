namespace Veriado.Infrastructure.Persistence.Entities;

/// <summary>
/// Stores the absolute storage root path used to resolve physical file locations.
/// </summary>
public sealed class FileStorageRootEntity
{
    /// <summary>
    /// Gets the primary key. Only a single row is expected (Id = 1).
    /// </summary>
    public int Id { get; private set; }

    /// <summary>
    /// Gets the absolute storage root path.
    /// </summary>
    public string RootPath { get; private set; } = string.Empty;
}
