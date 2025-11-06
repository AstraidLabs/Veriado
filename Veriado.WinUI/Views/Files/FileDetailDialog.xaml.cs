using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views.Files;

public sealed partial class FileDetailDialog : ContentDialog
{
    public FileDetailDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
        SecondaryButtonClick += OnSecondaryButtonClick;
    }

    public FileDetailDialogViewModel? ViewModel => DataContext as FileDetailDialogViewModel;

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (ViewModel is null)
        {
            return;
        }

        args.Cancel = true;
        if (await ViewModel.ExecuteSaveAsync())
        {
            sender.Hide();
        }
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (ViewModel is null)
        {
            return;
        }

        args.Cancel = true;
        ViewModel.CancelCommand.Execute(null);
    }
}
