using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Aggregates the primary feature view models exposed by the shell.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(
        FilesGridViewModel filesGridViewModel,
        FileDetailViewModel fileDetailViewModel,
        ImportViewModel importViewModel)
    {
        Files = filesGridViewModel;
        Detail = fileDetailViewModel;
        Import = importViewModel;
    }

    /// <summary>
    /// Gets the grid view model that exposes the paged catalogue.
    /// </summary>
    public FilesGridViewModel Files { get; }

    /// <summary>
    /// Gets the detail view model used to present individual file information.
    /// </summary>
    public FileDetailViewModel Detail { get; }

    /// <summary>
    /// Gets the import workflow view model.
    /// </summary>
    public ImportViewModel Import { get; }
}
