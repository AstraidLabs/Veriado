using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Veriado.Application.Abstractions;
using Veriado.Application.Common;
using Veriado.Contracts.Files;

namespace Veriado.Services.Files;

/// <summary>
/// Implements helpers for retrieving and persisting file binary content.
/// </summary>
public sealed class FileContentService : IFileContentService
{
    private readonly IFileRepository _repository;
    private readonly IMapper _mapper;

    public FileContentService(IFileRepository repository, IMapper mapper)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task<FileContentResponseDto?> GetContentAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await _repository.GetAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return null;
        }

        var summary = _mapper.Map<FileSummaryDto>(file);
        var content = new byte[file.Content.Bytes.Length];
        Array.Copy(file.Content.Bytes, content, file.Content.Bytes.Length);
        return new FileContentResponseDto(
            summary.Id,
            summary.Name,
            summary.Extension,
            summary.Mime,
            summary.Author,
            summary.Size,
            summary.Version,
            summary.IsReadOnly,
            summary.CreatedUtc,
            summary.LastModifiedUtc,
            summary.Validity,
            content);
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

        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(targetPath, file.Content.Bytes, cancellationToken).ConfigureAwait(false);
            return AppResult<Guid>.Success(file.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AppResult<Guid>.FromException(ex, "Failed to save file content to disk.");
        }
    }
}
