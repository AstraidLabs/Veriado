namespace Veriado.WinUI.Services.Abstractions;

public interface ISettingsService
{
    Task<AppSettings> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task UpdateAsync(Action<AppSettings> updateAction, CancellationToken cancellationToken = default);
}

public sealed class AppSettings
{
    public const int DefaultPageSize = 50;

    public AppTheme Theme { get; set; } = AppTheme.Default;

    public int PageSize { get; set; } = DefaultPageSize;

    public string? Language { get; set; }
        = null;

    public string? LastFolder { get; set; }
        = null;

    public string? LastQuery { get; set; }
        = null;

    public ImportPreferences Import { get; set; } = new();
}

public sealed class ImportPreferences
{
    public bool? Recursive { get; set; }
        = null;

    public bool? KeepFsMetadata { get; set; }
        = null;

    public bool? SetReadOnly { get; set; }
        = null;

    public bool? UseParallel { get; set; }
        = null;

    public int? MaxDegreeOfParallelism { get; set; }
        = null;

    public string? DefaultAuthor { get; set; }
        = null;

    public double? MaxFileSizeMegabytes { get; set; }
        = null;
}
