using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Aggregates the primary feature view models exposed by the shell.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly Lazy<FilesGridViewModel> _files;
    private readonly Lazy<FileDetailViewModel> _detail;
    private readonly Lazy<ImportViewModel> _import;

    public ShellViewModel(
        Func<FilesGridViewModel> filesFactory,
        Func<FileDetailViewModel> detailFactory,
        Func<ImportViewModel> importFactory)
    {
        ArgumentNullException.ThrowIfNull(filesFactory);
        ArgumentNullException.ThrowIfNull(detailFactory);
        ArgumentNullException.ThrowIfNull(importFactory);

        _files = new Lazy<FilesGridViewModel>(filesFactory);
        _detail = new Lazy<FileDetailViewModel>(detailFactory);
        _import = new Lazy<ImportViewModel>(importFactory);
    }

    /// <summary>
    /// Gets the grid view model that exposes the paged catalogue.
    /// </summary>
    public FilesGridViewModel Files => _files.Value;

    /// <summary>
    /// Gets the detail view model used to present individual file information.
    /// </summary>
    public FileDetailViewModel Detail => _detail.Value;

    /// <summary>
    /// Gets the import workflow view model.
    /// </summary>
    public ImportViewModel Import => _import.Value;
}
