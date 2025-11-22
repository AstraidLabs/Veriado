// File: Veriado.Services/Files/FileContentService.cs
using System.Diagnostics;
using System.IO;
using AutoMapper;
using Veriado.Domain.Files;
using Veriado.Domain.FileSystem;

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

    public async Task<FileContentResponseDto?> GetContentAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var resolution = await ResolvePhysicalFileAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (resolution.IsFailure)
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(resolution.Value.FullPath, cancellationToken).ConfigureAwait(false);
        var dto = _mapper.Map<FileContentResponseDto>(resolution.Value.File);
        return dto with { Content = bytes };
    }

    public async Task<FileContentLocationDto?> GetContentLocationAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolvePhysicalFileAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (resolution.IsFailure)
        {
            return null;
        }

        return CreateLocationDto(resolution.Value);
    }

    public async Task<AppResult<Guid>> SaveContentToDiskAsync(
        Guid fileId,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return AppResult<Guid>.Validation(new[] { "Target path is required." });
        }

        var resolution = await ResolvePhysicalFileAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (resolution.IsFailure)
        {
            return AppResult<Guid>.Failure(resolution.Error);
        }

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory) && !Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory!);
        }

        File.Copy(resolution.Value.FullPath, targetPath, overwrite: true);
        return AppResult<Guid>.Success(fileId);
    }

    public Task<AppResult<Guid>> ExportContentAsync(
        Guid fileId,
        string targetPath,
        CancellationToken cancellationToken = default) =>
        SaveContentToDiskAsync(fileId, targetPath, cancellationToken);

    public async Task<AppResult<Guid>> OpenInDefaultAppAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolvePhysicalFileAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (resolution.IsFailure)
        {
            return AppResult<Guid>.Failure(resolution.Error);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = resolution.Value.FullPath,
                UseShellExecute = true,
            };

            _ = Process.Start(startInfo);
            return AppResult<Guid>.Success(fileId);
        }
        catch (Exception ex)
        {
            return AppResult<Guid>.FromException(ex, "Unable to open the file in the default application.");
        }
    }

    public async Task<AppResult<Guid>> ShowInFileExplorerAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolvePhysicalFileAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (resolution.IsFailure)
        {
            return AppResult<Guid>.Failure(resolution.Error);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{resolution.Value.FullPath}\"",
                UseShellExecute = true,
            };

            _ = Process.Start(startInfo);
            return AppResult<Guid>.Success(fileId);
        }
        catch (Exception ex)
        {
            return AppResult<Guid>.FromException(ex, "Unable to show the file in File Explorer.");
        }
    }

    private static FileContentLocationDto CreateLocationDto(ResolvedContentLocation resolved)
    {
        var fileInfo = new FileInfo(resolved.FullPath);
        var createdUtc = fileInfo.Exists
            ? new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero)
            : resolved.FileSystem.CreatedUtc.ToDateTimeOffset();
        var lastWriteUtc = fileInfo.Exists
            ? new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero)
            : resolved.FileSystem.LastWriteUtc.ToDateTimeOffset();
        var lastAccessUtc = fileInfo.Exists
            ? new DateTimeOffset(fileInfo.LastAccessTimeUtc, TimeSpan.Zero)
            : resolved.FileSystem.LastAccessUtc.ToDateTimeOffset();

        return new FileContentLocationDto
        {
            FileId = resolved.File.Id,
            FullPath = resolved.FullPath,
            Mime = resolved.FileSystem.Mime.Value,
            SizeBytes = fileInfo.Exists ? fileInfo.Length : resolved.FileSystem.Size.Value,
            CreatedUtc = createdUtc,
            LastWriteUtc = lastWriteUtc,
            LastAccessUtc = lastAccessUtc,
            Hash = resolved.FileSystem.Hash.Value,
            StorageProvider = resolved.FileSystem.Provider.ToString(),
            PhysicalState = resolved.FileSystem.PhysicalState.ToString(),
            IsEncrypted = resolved.FileSystem.IsEncrypted,
        };
    }

    private async Task<AppResult<ResolvedContentLocation>> ResolvePhysicalFileAsync(
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var file = await _repository.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return AppResult<ResolvedContentLocation>.NotFound($"File '{fileId}' was not found.");
        }

        var fileSystem = await _repository.GetFileSystemAsync(file.FileSystemId, cancellationToken)
            .ConfigureAwait(false);
        if (fileSystem is null)
        {
            return AppResult<ResolvedContentLocation>.NotFound($"File system entry for '{fileId}' was not found.");
        }

        var fullPath = _pathResolver.GetFullPath(fileSystem.RelativePath.Value);
        if (!File.Exists(fullPath))
        {
            return AppResult<ResolvedContentLocation>.NotFound(
                $"Physical file for '{fileId}' was not found at '{fullPath}'.");
        }

        return AppResult<ResolvedContentLocation>.Success(new ResolvedContentLocation(file, fileSystem, fullPath));
    }

    private sealed record ResolvedContentLocation(FileEntity File, FileSystemEntity FileSystem, string FullPath);
}
