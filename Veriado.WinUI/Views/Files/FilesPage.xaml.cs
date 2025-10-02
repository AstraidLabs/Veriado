using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
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
    private ImplicitAnimationCollection? _itemAnimations;

    public FilesPage(FilesPageViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        InitializeComponent();
        FilesRepeater.ElementPrepared += OnFilesRepeaterElementPrepared;
        FilesRepeater.ElementClearing += OnFilesRepeaterElementClearing;
        UpdateLoadingState(ViewModel.IsBusy, animate: false);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public FilesPageViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.StartHealthMonitoring();
        await ExecuteInitialRefreshAsync().ConfigureAwait(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.StopHealthMonitoring();
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
            return;
        }

        LoadingRing.Visibility = Visibility.Visible;
        ResultsHost.Visibility = Visibility.Visible;
        ResultsHost.IsHitTestVisible = !isBusy;

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

    private void OnFilesRepeaterElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not UIElement element)
        {
            return;
        }

        if (!AnimationSettings.AreEnabled)
        {
            element.Opacity = 1d;
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.Opacity = 0f;
        visual.Translation = new Vector3(0f, 12f, 0f);

        if (_itemAnimations is null)
        {
            var compositor = visual.Compositor;
            var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Medium);
            var easing = AnimationResourceHelper.CreateEasing(compositor, AnimationResourceKeys.EaseOut);

            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.Target = nameof(Visual.Opacity);
            fadeAnimation.Duration = duration;
            fadeAnimation.InsertKeyFrame(0f, 0f);
            fadeAnimation.InsertKeyFrame(1f, 1f, easing);

            var translationAnimation = compositor.CreateVector3KeyFrameAnimation();
            translationAnimation.Target = nameof(Visual.Translation);
            translationAnimation.Duration = duration;
            translationAnimation.InsertKeyFrame(0f, new Vector3(0f, 12f, 0f));
            translationAnimation.InsertKeyFrame(1f, Vector3.Zero, easing);

            _itemAnimations = compositor.CreateImplicitAnimationCollection();
            _itemAnimations[nameof(Visual.Opacity)] = fadeAnimation;
            _itemAnimations[nameof(Visual.Translation)] = translationAnimation;
        }

        visual.ImplicitAnimations = _itemAnimations;
        visual.Opacity = 1f;
        visual.Translation = Vector3.Zero;
    }

    private void OnFilesRepeaterElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is UIElement element)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.Opacity = 1f;
            visual.Translation = Vector3.Zero;
        }
    }

    private void OnInfoBarOpened(object sender, object e)
    {
        if (sender is not InfoBar infoBar)
        {
            return;
        }

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
