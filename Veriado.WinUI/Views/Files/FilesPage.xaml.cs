using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Veriado.Contracts.Files;
using Veriado.WinUI.Helpers;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views.Files;

public sealed partial class FilesPage : Page
{
    private readonly IFilesSearchSuggestionsProvider _suggestionsProvider;
    private readonly IServerClock _serverClock;
    private readonly DispatcherTimer _validityRefreshTimer;
    private CancellationTokenSource? _suggestionRequestSource;

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
        _validityRefreshTimer.Tick += OnValidityRefreshTimerTick;
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public FilesPageViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _validityRefreshTimer.Start();
        UpdateValidityBadges();
        ViewModel.StartHealthMonitoring();
        await ExecuteInitialRefreshAsync().ConfigureAwait(true);
        UpdateValidityBadges();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.StopHealthMonitoring();
        CancelSuggestionRequest();
        if (_validityRefreshTimer.IsEnabled)
        {
            _validityRefreshTimer.Stop();
        }
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

    public Visibility GetValidityVisibility(FileSummaryDto? file)
    {
        return file?.Validity is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public string GetValidityText(FileSummaryDto? file)
    {
        if (!FileValidityHelper.TryGetBadge(file?.Validity, _serverClock.NowLocal, out var badge))
        {
            return string.Empty;
        }

        return badge.Text;
    }

    public Brush GetValidityBackgroundBrush(FileSummaryDto? file)
    {
        var badge = FileValidityHelper.GetBadge(file?.Validity, _serverClock.NowLocal);
        return badge.Background;
    }

    public Brush GetValidityForegroundBrush(FileSummaryDto? file)
    {
        var badge = FileValidityHelper.GetBadge(file?.Validity, _serverClock.NowLocal);
        return badge.Foreground;
    }

    public string? GetValidityTooltip(FileSummaryDto? file)
    {
        if (file?.Validity is not { } validity)
        {
            return null;
        }

        var culture = CultureInfo.CurrentCulture;
        var issued = validity.IssuedAt.ToLocalTime();
        var validUntil = validity.ValidUntil.ToLocalTime();
        return $"Platnost: {issued.ToString("d", culture)} – {validUntil.ToString("d", culture)}";
    }

    public bool TryGetDaysRemaining(FileSummaryDto? file, out int daysRemaining)
    {
        daysRemaining = 0;

        if (FileValidityHelper.TryGetBadge(file?.Validity, _serverClock.NowLocal, out var badge)
            && badge.DaysRemaining is { } remaining)
        {
            daysRemaining = remaining;
            return true;
        }

        return false;
    }

    private void OnValidityRefreshTimerTick(object? sender, object e)
    {
        UpdateValidityBadges();
    }

    private void UpdateValidityBadges()
    {
        Bindings?.Update();
    }
}
