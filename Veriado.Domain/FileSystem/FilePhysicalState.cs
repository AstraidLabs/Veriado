namespace Veriado.Domain.FileSystem;

/// <summary>
/// Describes the observed physical health of a file on disk.
/// </summary>
public enum FilePhysicalState
{
    Unknown = 0,
    Healthy = 1,
    Missing = 2,
    MovedOrRenamed = 3,
    ContentChanged = 4,
}
