namespace Veriado.Services.Storage;

public sealed class StorageSettingsDto
{
    public string? CurrentRootPath { get; init; }

    public bool CanChangeRoot { get; init; }
}

public enum ChangeStorageRootResult
{
    Success,
    CatalogNotEmpty,
    InvalidPath,
    IoError,
    UnknownError,
}

public interface IStorageSettingsService
{
    Task<StorageSettingsDto> GetStorageSettingsAsync(CancellationToken cancellationToken = default);

    Task<ChangeStorageRootResult> ChangeStorageRootAsync(string newRootPath, CancellationToken cancellationToken = default);
}
