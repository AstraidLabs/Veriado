namespace Veriado.Infrastructure.Concurrency;

/// <summary>
/// Represents contextual information about a file persistence operation queued for the write worker.
/// </summary>
internal readonly record struct QueuedFileWrite(FileEntity Entity, FilePersistenceOptions Options);
