using Veriado.Domain.FileSystem;

namespace Veriado.Infrastructure.FileSystem;

public interface IFilePathResolver
{
    string GetStorageRoot();
    string GetFullPath(string relativePath);
    string GetFullPath(string relativePath, string? rootOverride);
    string GetFullPath(FileSystemEntity file);
    string GetRelativePath(string fullPath);
}
