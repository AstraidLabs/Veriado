using System;
using System.Linq;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Veriado.WinUI.ViewModels;

namespace Veriado.WinUI.Views;

public sealed partial class FilesPage : Page
{
    public FilesPage()
    {
        InitializeComponent();
        ViewModel = AppHost.Services.GetRequiredService<FilesGridViewModel>();
        DataContext = ViewModel;
        ViewModel.DetailRequested += OnDetailRequested;
    }

    public FilesGridViewModel ViewModel { get; }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;
        await ViewModel.InitializeAsync().ConfigureAwait(true);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.DetailRequested -= OnDetailRequested;
    }

    private void OnSuggestionChosen(RichSuggestBox sender, RichSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string suggestion)
        {
            ViewModel.QueryText = suggestion;
        }
    }

    private void OnQuerySubmitted(RichSuggestBox sender, RichSuggestBoxQuerySubmittedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            ViewModel.QueryText = args.QueryText;
        }
    }

    private async void OnScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var threshold = scrollViewer.ExtentHeight - scrollViewer.ViewportHeight - 200;
        if (scrollViewer.VerticalOffset >= threshold)
        {
            await ViewModel.EnsureMoreItemsAsync(System.Threading.CancellationToken.None).ConfigureAwait(true);
        }
    }

    private void OnDetailRequested(object? sender, Guid fileId)
    {
        if (ViewModel.Items is null)
        {
            return;
        }

        var index = ViewModel.Items
            .Select((item, idx) => (item, idx))
            .FirstOrDefault(tuple => tuple.item.Id == fileId);

        FrameworkElement? container = null;
        if (index.item is not null)
        {
            container = FilesRepeater.TryGetElement(index.idx) as FrameworkElement;
        }

        if (container is not null)
        {
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("FileCardAnimation", container);
        }

        Frame?.Navigate(typeof(FileDetailPage), fileId, new DrillInNavigationTransitionInfo());
    }
}
