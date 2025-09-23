using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.ViewModels.Base;

namespace Veriado.ViewModels.Import;

public sealed partial class ImportViewModel : ViewModelBase
{
    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private bool isCompleted;

    [RelayCommand]
    private async Task StartImportAsync()
    {
        await SafeExecuteAsync(async ct =>
        {
            for (var i = 0; i <= 10; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(120, ct).ConfigureAwait(false);
                Progress = i * 10;
            }

            IsCompleted = true;
        }, "Import probíhá…");

        StatusMessage = IsCompleted ? "Import dokončen." : StatusMessage;
    }

    [RelayCommand]
    private void Reset()
    {
        Progress = 0;
        IsCompleted = false;
        StatusMessage = null;
        HasError = false;
    }
}
