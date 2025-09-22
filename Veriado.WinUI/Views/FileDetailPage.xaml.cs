using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Veriado.WinUI.ViewModels;

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
            await ViewModel.LoadAsync(fileId);
            var animation = ConnectedAnimationService.GetForCurrentView().GetAnimation("FileCardAnimation");
            animation?.TryStart(DetailHeader);
        }
    }
}
