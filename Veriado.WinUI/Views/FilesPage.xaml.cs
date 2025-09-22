using System;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Veriado.Contracts.Files;
using Veriado.WinUI.ViewModels;

namespace Veriado.WinUI.Views;

public sealed partial class FilesPage : Page
{
    public FilesPage()
    {
        InitializeComponent();
        ViewModel = AppHost.Services.GetRequiredService<FilesGridViewModel>();
        DataContext = ViewModel;
    }

    public FilesGridViewModel ViewModel { get; }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;
        await ViewModel.InitializeAsync();
    }

    private void OnSuggestionChosen(RichSuggestBox sender, RichSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string suggestion)
        {
            ViewModel.ApplySuggestionCommand.Execute(suggestion);
        }
    }

    private void OnQuerySubmitted(RichSuggestBox sender, RichSuggestBoxQuerySubmittedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            ViewModel.SearchText = args.QueryText;
        }
    }

    private void OnGridItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not FileSummaryDto summary)
        {
            return;
        }

        var container = FilesGrid.ContainerFromItem(summary) as FrameworkElement;
        if (container is not null)
        {
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("FileCardAnimation", container);
        }

        Frame?.Navigate(typeof(FileDetailPage), summary.Id, new DrillInNavigationTransitionInfo());
    }
}
