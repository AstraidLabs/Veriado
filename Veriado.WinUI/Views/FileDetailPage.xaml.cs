using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Veriado.ViewModels.Files;

namespace Veriado.Views;

public sealed partial class FileDetailPage : Page
{
    public FileDetailViewModel ViewModel { get; }

    public FileDetailPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<FileDetailViewModel>();
        DataContext = ViewModel;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is Guid fileId)
        {
            await ViewModel.LoadCommand.ExecuteAsync(fileId);
        }
    }
}
