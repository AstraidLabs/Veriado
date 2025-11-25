using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views.Files;

public sealed partial class FilesPage : Page
{
    private readonly IFilesSearchSuggestionsProvider _suggestionsProvider;
    private readonly IServerClock _serverClock;
    private readonly DispatcherTimer _validityRefreshTimer;
    private CancellationTokenSource? _suggestionRequestSource;
    private bool _itemsCollectionHooked;

    public FilesPage(
        FilesPageViewModel viewModel,
        IFilesSearchSuggestionsProvider suggestionsProvider,
        IServerClock serverClock)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _suggestionsProvider = suggestionsProvider ?? throw new ArgumentNullException(nameof(suggestionsProvider));
        _serverClock = serverClock ?? throw new ArgumentNullException(nameof(serverClock));
        _validityRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1),
        };
        _validityRefreshTimer.Tick += OnValidityTick;
        DataContext = ViewModel;
        HookItemsCollection();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public FilesPageViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _validityRefreshTimer.Start();
        RefreshValidityIndicators();
        _ = ViewModel.StartHealthMonitoringAsync();
        await ExecuteInitialRefreshAsync().ConfigureAwait(true);
        RefreshValidityIndicators();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.StopHealthMonitoringAsync().ConfigureAwait(true);
        CancelSuggestionRequest();
        if (_validityRefreshTimer.IsEnabled)
        {
            _validityRefreshTimer.Stop();
        }
        UnhookItemsCollection();
    }

    private Task ExecuteInitialRefreshAsync()
    {
        return ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private async void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        try
        {
            CancelSuggestionRequest();

            var cts = new CancellationTokenSource();
            _suggestionRequestSource = cts;

            IReadOnlyList<string> suggestions;
            try
            {
                suggestions = await _suggestionsProvider.GetSuggestionsAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            var query = sender.Text?.Trim();
            IEnumerable<string> filtered = suggestions;

            if (!string.IsNullOrEmpty(query))
            {
                filtered = suggestions.Where(suggestion => suggestion.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            sender.ItemsSource = filtered.ToArray();
        }
        catch (OperationCanceledException)
        {
            // Intentionally ignored.
        }
        catch
        {
            sender.ItemsSource = Array.Empty<string>();
        }
    }

    private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.ChosenSuggestion as string ?? args.QueryText ?? string.Empty;
        var normalized = string.IsNullOrWhiteSpace(query) ? null : query.Trim();

        if (!string.Equals(ViewModel.SearchText, normalized, StringComparison.Ordinal))
        {
            ViewModel.SearchText = normalized;
        }

        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private void CancelSuggestionRequest()
    {
        var source = Interlocked.Exchange(ref _suggestionRequestSource, null);
        if (source is null)
        {
            return;
        }

        if (!source.IsCancellationRequested)
        {
            source.Cancel();
        }

        source.Dispose();
    }

    private void OnValidityTick(object? sender, object e)
    {
        RefreshValidityIndicators();
    }

    private void RefreshValidityIndicators()
    {
        var now = _serverClock.NowLocal;
        ViewModel.RefreshValidityStates(now);
    }

    private void HookItemsCollection()
    {
        if (_itemsCollectionHooked)
        {
            return;
        }

        if (ViewModel.Items is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged += OnItemsCollectionChanged;
            _itemsCollectionHooked = true;
        }
    }

    private void UnhookItemsCollection()
    {
        if (!_itemsCollectionHooked)
        {
            return;
        }

        if (ViewModel.Items is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged -= OnItemsCollectionChanged;
        }

        _itemsCollectionHooked = false;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null || e.NewItems.Count == 0)
        {
            return;
        }

        var now = _serverClock.NowLocal;
        foreach (var item in e.NewItems.OfType<FileListItemModel>())
        {
            item.RecomputeValidity(now, ViewModel.ValidityThresholds);
        }
    }
}
