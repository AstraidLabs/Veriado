using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace Veriado.WinUI.ViewModels.Files;

/// <summary>
/// Represents the filter state applied to the files grid.
/// </summary>
public sealed partial class FileFiltersViewModel : ViewModelBase
{
    public FileFiltersViewModel(IMessenger messenger)
        : base(messenger)
    {
    }

    [ObservableProperty]
    private double sizeFrom;

    [ObservableProperty]
    private double sizeTo = 1024 * 1024 * 100;

    [ObservableProperty]
    private DateTimeOffset? createdFrom;

    [ObservableProperty]
    private DateTimeOffset? createdTo;
}
