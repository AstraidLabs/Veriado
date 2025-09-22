using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Veriado.WinUI.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Veriado.WinUI.Views;

public sealed partial class FileDetailPage : Page
{
    public FileDetailPage()
    {
        InitializeComponent();
        ViewModel = AppHost.Services.GetRequiredService<FileDetailViewModel>();
        DataContext = ViewModel;
    }

    public FileDetailViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is Guid fileId)
        {
            await ViewModel.GetDetailAsync(fileId).ConfigureAwait(true);
            var animation = ConnectedAnimationService.GetForCurrentView().GetAnimation("FileCardAnimation");
            animation?.TryStart(DetailHeader);
        }
    }

    private async void OnReplaceContentClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentFileId is null)
        {
            return;
        }

        var window = App.MainWindowInstance ?? throw new InvalidOperationException("Main window is not available.");
        var hwnd = WindowNative.GetWindowHandle(window);

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenStreamForReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        await ViewModel.ReplaceContentAsync(buffer.ToArray()).ConfigureAwait(true);
    }
}
