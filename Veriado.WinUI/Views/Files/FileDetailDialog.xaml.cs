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
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        args.Cancel = true;
        var deferral = args.GetDeferral();
        try
        {
            if (await viewModel.ExecuteSaveAsync())
            {
                sender.Hide();
            }
        }
        finally
        {
            deferral.Complete();
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
