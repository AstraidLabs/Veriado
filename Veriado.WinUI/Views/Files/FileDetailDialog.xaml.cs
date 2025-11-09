using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Helpers;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views.Files;

public sealed partial class FileDetailDialog : ContentDialog
{
    private const double NarrowThreshold = 680.0;

    public FileDetailDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
        SecondaryButtonClick += OnSecondaryButtonClick;
        Loaded += OnLoaded;
        LayoutRoot.SizeChanged += OnLayoutRootSizeChanged;
    }

    public FileDetailDialogViewModel? ViewModel => DataContext as FileDetailDialogViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWidthState();
    }

    private void OnLayoutRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyWidthState();
    }

    private void ApplyWidthState()
    {
        var width = ResponsiveHelper.GetEffectiveWidth(LayoutRoot);
        var targetState = width < NarrowThreshold ? "Narrow" : "Wide";
        _ = VisualStateManager.GoToState(this, targetState, true);
    }

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
