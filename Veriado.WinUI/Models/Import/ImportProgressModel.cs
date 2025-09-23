using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Models.Import;

/// <summary>
/// Represents the UI-facing progress of a long-running import operation.
/// </summary>
public sealed partial class ImportProgressModel : ObservableObject
{
    [ObservableProperty]
    private int total;

    [ObservableProperty]
    private int processed;

    [ObservableProperty]
    private int errorsCount;

    [ObservableProperty]
    private string? currentPath;

    [ObservableProperty]
    private bool isRunning;

    /// <summary>
    /// Gets the completion percentage for the current batch.
    /// </summary>
    public double Percent => Total == 0 ? 0 : (double)Processed / Total * 100.0;

    /// <summary>
    /// Gets a value indicating whether any errors occurred.
    /// </summary>
    public bool HasErrors => ErrorsCount > 0;

    partial void OnTotalChanged(int value) => OnPropertyChanged(nameof(Percent));

    partial void OnProcessedChanged(int value) => OnPropertyChanged(nameof(Percent));
}
