// BEGIN CHANGE Veriado.WinUI/ViewModels/MainViewModel.cs
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.WinUI.Messages;

namespace Veriado.WinUI.ViewModels;

/// <summary>
/// Root view model orchestrating shell state and coordinating child view models.
/// </summary>
public sealed partial class MainViewModel : BaseViewModel, IRecipient<ImportProgressMessage>
{
    public MainViewModel(ImportViewModel import, GridViewModel grid, DetailViewModel detail, IMessenger messenger)
        : base(messenger)
    {
        Import = import;
        Grid = grid;
        Detail = detail;
    }

    public ImportViewModel Import { get; }

    public GridViewModel Grid { get; }

    public DetailViewModel Detail { get; }

    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    private bool isProgressIndeterminate;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private string? progressMessage;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Grid.EnsureDataAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Receive(ImportProgressMessage message)
    {
        var progress = message.Value;
        ProgressMessage = progress.Message;
        IsProgressVisible = !progress.IsCompleted;
        IsProgressIndeterminate = progress.IsIndeterminate;

        if (progress.ProgressValue.HasValue)
        {
            ProgressValue = progress.ProgressValue.Value;
        }

        if (progress.IsCompleted)
        {
            IsBusy = false;
        }
        else
        {
            IsBusy = true;
        }

        StatusMessage = progress.Message;
    }
}
// END CHANGE Veriado.WinUI/ViewModels/MainViewModel.cs
