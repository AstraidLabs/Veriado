namespace Veriado.Services.Import.Ingestion;

/// <summary>
/// Controls how source files are shared with other processes while being imported.
/// </summary>
public enum FileOpenSharePolicy
{
    /// <summary>
    /// Opens the file for read-only access while allowing other readers.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Opens the file for reading while allowing other readers and writers.
    /// </summary>
    ReadWrite,

    /// <summary>
    /// Opens the file for reading while allowing writers and delete operations.
    /// </summary>
    ReadWriteDelete,
}
