// File: Veriado.Services/Files/FileContentService.cs
using System.IO;
using AutoMapper;

namespace Veriado.Services.Files;

/// <summary>
/// Implements helpers for retrieving and persisting file binary content.
/// </summary>
public sealed class FileContentService : IFileContentService
{
    private readonly IFileRepository _repository;
    private readonly IFilePathResolver _pathResolver;
    private readonly IMapper _mapper;

    public FileContentService(IFileRepository repository, IFilePathResolver pathResolver, IMapper mapper)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task<FileContentResponseDto?> GetContentAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await _repository.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return null;
        }

        var fileSystem = await _repository.GetFileSystemAsync(file.FileSystemId, cancellationToken)
            .ConfigureAwait(false);
        if (fileSystem is null)
        {
            return null;
        }

        var absolutePath = _pathResolver.GetFullPath(fileSystem.RelativePath.Value);
        if (!File.Exists(absolutePath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        var dto = _mapper.Map<FileContentResponseDto>(file);
        return dto with { Content = bytes };
    }

    public async Task<AppResult<Guid>> SaveContentToDiskAsync(Guid fileId, string targetPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return AppResult<Guid>.Validation(new[] { "Target path is required." });
        }

        var file = await _repository.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return AppResult<Guid>.NotFound($"File '{fileId}' was not found.");
        }

        var fileSystem = await _repository.GetFileSystemAsync(file.FileSystemId, cancellationToken)
            .ConfigureAwait(false);
        if (fileSystem is null)
        {
            return AppResult<Guid>.NotFound($"File system entry for '{fileId}' was not found.");
        }

        var sourcePath = _pathResolver.GetFullPath(fileSystem.RelativePath.Value);
        if (!File.Exists(sourcePath))
        {
            return AppResult<Guid>.NotFound($"Physical file for '{fileId}' was not found at '{sourcePath}'.");
        }

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory) && !Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory!);
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
        return AppResult<Guid>.Success(fileId);
    }
}
