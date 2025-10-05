using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Veriado.WinUI.Helpers;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views.Files;

public sealed partial class FilesPage : Page
{
    private readonly HashSet<InfoBar> _closingInfoBars = new();
    private readonly Dictionary<InfoBar, long> _infoBarIsOpenCallbacks = new();

    public FilesPage()
        : this(App.Services.GetRequiredService<FilesPageViewModel>())
    {
    }

    public FilesPage(FilesPageViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        InitializeComponent();
        UpdateListAnimations(AnimationSettings.AreEnabled);
        UpdateLoadingState(ViewModel.IsBusy, animate: false);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        AnimationSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
    }

    public FilesPageViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.StartHealthMonitoring();

        try
        {
            await Task.WhenAll(
                ExecuteInitialRefreshAsync(),
                ViewModel.LoadSearchSuggestionsAsync(CancellationToken.None)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize {nameof(FilesPage)}: {ex}");
            ViewModel.HasError = true;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.StopHealthMonitoring();
        AnimationSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
    }

    private Task ExecuteInitialRefreshAsync()
    {
        return ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilesPageViewModel.IsBusy))
        {
            _ = DispatcherQueue.TryEnqueue(() => UpdateLoadingState(ViewModel.IsBusy, animate: true));
        }
    }

    private void UpdateLoadingState(bool isBusy, bool animate)
    {
        if (!animate || !AnimationSettings.AreEnabled)
        {
            LoadingRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            LoadingRing.Opacity = isBusy ? 1d : 0d;
            ResultsHost.Opacity = isBusy ? 0d : 1d;
            ResultsHost.IsHitTestVisible = !isBusy;
            FilesScrollViewer.IsHitTestVisible = !isBusy;
            return;
        }

        LoadingRing.Visibility = Visibility.Visible;
        ResultsHost.Visibility = Visibility.Visible;
        ResultsHost.IsHitTestVisible = !isBusy;
        FilesScrollViewer.IsHitTestVisible = !isBusy;

        var resultsVisual = ElementCompositionPreview.GetElementVisual(ResultsHost);
        var loadingVisual = ElementCompositionPreview.GetElementVisual(LoadingRing);
        var compositor = resultsVisual.Compositor;
        var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Medium);
        var easing = AnimationResourceHelper.CreateEasing(compositor, AnimationResourceKeys.EaseOut);

        AnimateOpacity(compositor, loadingVisual, isBusy ? 1f : 0f, duration, easing, () =>
        {
            if (!isBusy)
            {
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        });

        AnimateOpacity(compositor, resultsVisual, isBusy ? 0f : 1f, duration, easing, null);
    }

    private void UpdateListAnimations(bool areEnabled)
    {
        if (FilesRepeater is null)
        {
            return;
        }

        ImplicitListAnimations.Attach(FilesRepeater, areEnabled);
    }

    private void OnAnimationsEnabledChanged(object? sender, bool areEnabled)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            UpdateListAnimations(areEnabled);
            UpdateLoadingState(ViewModel.IsBusy, animate: false);
        });
    }

    private static void AnimateOpacity(Compositor compositor, Visual visual, float targetOpacity, TimeSpan duration, CompositionEasingFunction easing, Action? completed)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Target = nameof(Visual.Opacity);
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, visual.Opacity);
        animation.InsertKeyFrame(1f, targetOpacity, easing);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation(nameof(Visual.Opacity), animation);
        if (completed is not null)
        {
            batch.Completed += (_, __) => completed();
        }
        batch.End();
    }

    private void OnInfoBarLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not InfoBar infoBar)
        {
            return;
        }

        infoBar.Unloaded -= OnInfoBarUnloaded;
        infoBar.Unloaded += OnInfoBarUnloaded;

        if (!_infoBarIsOpenCallbacks.ContainsKey(infoBar))
        {
            var token = infoBar.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, OnInfoBarIsOpenChanged);
            _infoBarIsOpenCallbacks[infoBar] = token;
        }

        if (infoBar.IsOpen)
        {
            PlayInfoBarOpeningAnimation(infoBar);
        }
    }

    private void OnInfoBarUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not InfoBar infoBar)
        {
            return;
        }

        infoBar.Unloaded -= OnInfoBarUnloaded;

        if (_infoBarIsOpenCallbacks.TryGetValue(infoBar, out var token))
        {
            infoBar.UnregisterPropertyChangedCallback(InfoBar.IsOpenProperty, token);
            _infoBarIsOpenCallbacks.Remove(infoBar);
        }
    }

    private void OnInfoBarIsOpenChanged(DependencyObject sender, DependencyProperty _)
    {
        if (sender is InfoBar infoBar && infoBar.IsOpen)
        {
            PlayInfoBarOpeningAnimation(infoBar);
        }
    }

    private void PlayInfoBarOpeningAnimation(InfoBar infoBar)
    {
        if (!AnimationSettings.AreEnabled)
        {
            infoBar.Opacity = 1d;
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(infoBar);
        var compositor = visual.Compositor;
        var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Fast);
        var easing = AnimationResourceHelper.CreateEasing(compositor, AnimationResourceKeys.EaseOut);

        visual.Opacity = 0f;

        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Target = nameof(Visual.Opacity);
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, 0f);
        animation.InsertKeyFrame(1f, 1f, easing);

        visual.StartAnimation(nameof(Visual.Opacity), animation);
    }

    private void OnInfoBarClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        if (!AnimationSettings.AreEnabled)
        {
            sender.Opacity = 0d;
            return;
        }

        if (_closingInfoBars.Contains(sender))
        {
            _closingInfoBars.Remove(sender);
            return;
        }

        args.Cancel = true;

        var visual = ElementCompositionPreview.GetElementVisual(sender);
        var compositor = visual.Compositor;
        var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Fast);
        var easing = AnimationResourceHelper.CreateEasing(compositor, AnimationResourceKeys.EaseOut);

        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Target = nameof(Visual.Opacity);
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, visual.Opacity);
        animation.InsertKeyFrame(1f, 0f, easing);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, __) =>
        {
            _closingInfoBars.Add(sender);
            sender.IsOpen = false;
        };
        visual.StartAnimation(nameof(Visual.Opacity), animation);
        batch.End();
    }
}
