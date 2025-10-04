using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Veriado.WinUI.Helpers;
using Veriado.WinUI.ViewModels.Import;

namespace Veriado.WinUI.Views.Import;

public sealed partial class ImportPage : Page
{
    private readonly HashSet<InfoBar> _closingInfoBars = new();
    private Compositor? _compositor;
    private bool _animationsEnabled = true;
    private CancellationTokenSource? _animationCts;
    private Visual? _dropZoneVisual;
    private Visual? _dropZoneHighlightVisual;
    private long _activeInfoBarToken = -1;
    private long _dynamicInfoBarToken = -1;

    public ImportPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<ImportPageViewModel>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private ImportPageViewModel? ViewModel => DataContext as ImportPageViewModel;

    private CancellationToken AnimationToken => _animationCts?.Token ?? CancellationToken.None;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ResetAnimationToken();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        _animationCts?.Cancel();
        _animationCts?.Dispose();
        _animationCts = null;

        if (ViewModel is { } vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.Log.CollectionChanged -= OnLogCollectionChanged;
        }

        DetachInfoBarHandlers();

        if (DropZoneHost is not null)
        {
            DropZoneHost.SizeChanged -= OnDropZoneSizeChanged;
        }

        AnimationSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeComposition();
        AttachInfoBarHandlers();
        SubscribeToViewModel();
        UpdateProgressState(immediate: true);
        UpdateSummaryState(immediate: true);
        UpdateQueueVisibility();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.Log.CollectionChanged -= OnLogCollectionChanged;
        }

        DetachInfoBarHandlers();

        if (DropZoneHost is not null)
        {
            DropZoneHost.SizeChanged -= OnDropZoneSizeChanged;
        }
    }

    private void InitializeComposition()
    {
        _compositor ??= ElementCompositionPreview.GetElementVisual(this).Compositor;

        AnimationSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        AnimationSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
        _animationsEnabled = AnimationSettings.AreEnabled;

        ResetAnimationToken();

        if (DropZoneHost is not null)
        {
            _dropZoneVisual = ElementCompositionPreview.GetElementVisual(DropZoneHost);
            _dropZoneVisual.Scale = Vector3.One;
            DropZoneHost.SizeChanged -= OnDropZoneSizeChanged;
            DropZoneHost.SizeChanged += OnDropZoneSizeChanged;
            UpdateDropZoneCenterPoint();
        }

        if (DropZoneHighlight is not null)
        {
            _dropZoneHighlightVisual = ElementCompositionPreview.GetElementVisual(DropZoneHighlight);
            _dropZoneHighlightVisual.Opacity = 0f;
        }

        if (DragOverlay is not null && _compositor is not null)
        {
            var overlayVisual = ElementCompositionPreview.GetElementVisual(DragOverlay);
            overlayVisual.Opacity = 0f;
        }

        UpdateQueueAnimations();
    }

    private void ResetAnimationToken()
    {
        _animationCts?.Cancel();
        _animationCts?.Dispose();
        _animationCts = new CancellationTokenSource();
    }

    private void OnAnimationsEnabledChanged(object? sender, bool areEnabled)
    {
        _animationsEnabled = areEnabled;
        UpdateQueueAnimations();
    }

    private void SubscribeToViewModel()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        vm.PropertyChanged -= OnViewModelPropertyChanged;
        vm.PropertyChanged += OnViewModelPropertyChanged;

        vm.Log.CollectionChanged -= OnLogCollectionChanged;
        vm.Log.CollectionChanged += OnLogCollectionChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImportPageViewModel.IsIndeterminate))
        {
            UpdateProgressState();
        }
        else if (e.PropertyName is nameof(ImportPageViewModel.IsImporting)
                 || e.PropertyName is nameof(ImportPageViewModel.Total)
                 || e.PropertyName is nameof(ImportPageViewModel.Processed))
        {
            UpdateSummaryState();
            UpdateQueueVisibility();
        }
    }

    private void OnLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DispatcherQueue?.HasThreadAccess == true)
        {
            UpdateQueueVisibility();
        }
        else
        {
            _ = DispatcherQueue?.TryEnqueue(UpdateQueueVisibility);
        }
    }

    private void UpdateQueueVisibility()
    {
        if (QueueSection is null)
        {
            return;
        }

        var hasItems = (ViewModel?.Log.Count ?? 0) > 0;
        var isImporting = ViewModel?.IsImporting ?? false;
        QueueSection.Visibility = (hasItems || isImporting) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateProgressState(bool immediate = false)
    {
        var showRing = ViewModel?.IsIndeterminate == true;
        _ = CrossFade(ExecutionProgressBar, PreparationProgressRing, !showRing, immediate);
    }

    private void UpdateSummaryState(bool immediate = false)
    {
        if (ViewModel is null)
        {
            return;
        }

        var hasResults = ViewModel.Processed > 0 || ViewModel.OkCount > 0 || ViewModel.ErrorCount > 0 || ViewModel.SkipCount > 0;
        var showSummary = !ViewModel.IsImporting && hasResults;
        _ = CrossFade(TotalProgressPanel, SummaryPanel, !showSummary, immediate);
    }

    private async Task CrossFade(UIElement? first, UIElement? second, bool showFirst, bool immediate = false)
    {
        if (first is null || second is null)
        {
            return;
        }

        var visibleElement = showFirst ? first : second;
        var hiddenElement = showFirst ? second : first;

        if (immediate || !_animationsEnabled || _compositor is null)
        {
            SetCrossFadeState(visibleElement, hiddenElement);
            return;
        }

        var token = AnimationToken;
        if (token.IsCancellationRequested)
        {
            return;
        }

        visibleElement.Visibility = Visibility.Visible;
        hiddenElement.Visibility = Visibility.Visible;
        visibleElement.IsHitTestVisible = true;
        hiddenElement.IsHitTestVisible = false;

        var visibleVisual = ElementCompositionPreview.GetElementVisual(visibleElement);
        var hiddenVisual = ElementCompositionPreview.GetElementVisual(hiddenElement);

        visibleVisual.Opacity = 0f;

        var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Medium);
        var fadeInEasing = AnimationResourceHelper.CreateEasing(_compositor, AnimationResourceKeys.EaseOut);
        var fadeOutEasing = AnimationResourceHelper.CreateEasing(_compositor, AnimationResourceKeys.EaseIn);

        var fadeIn = _compositor.CreateScalarKeyFrameAnimation();
        fadeIn.Duration = duration;
        fadeIn.InsertKeyFrame(1f, 1f, fadeInEasing);

        var fadeOut = _compositor.CreateScalarKeyFrameAnimation();
        fadeOut.Duration = duration;
        fadeOut.InsertKeyFrame(1f, 0f, fadeOutEasing);

        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visibleVisual.StartAnimation(nameof(Visual.Opacity), fadeIn);
        hiddenVisual.StartAnimation(nameof(Visual.Opacity), fadeOut);

        var tcs = new TaskCompletionSource<object?>();
        batch.Completed += (s, e) =>
        {
            SetCrossFadeState(visibleElement, hiddenElement);
            tcs.TrySetResult(null);
        };
        batch.End();

        using var registration = token.CanBeCanceled ? token.Register(() => tcs.TrySetCanceled(token)) : default;
        try
        {
            await tcs.Task.ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            // Swallow cancellations triggered by navigation.
        }
    }

    private static void SetCrossFadeState(UIElement visibleElement, UIElement hiddenElement)
    {
        visibleElement.Visibility = Visibility.Visible;
        visibleElement.Opacity = 1;
        visibleElement.IsHitTestVisible = true;

        hiddenElement.Opacity = 0;
        hiddenElement.Visibility = Visibility.Collapsed;
        hiddenElement.IsHitTestVisible = false;
    }

    private async Task AnimateDropZoneHoverAsync(bool isHovering, bool pulseOnDrop)
    {
        if (DropZoneHost is null)
        {
            return;
        }

        var highlightTarget = isHovering ? 0.35f : 0f;
        var scaleTarget = isHovering ? 1.02f : 1f;

        if (!_animationsEnabled || _compositor is null)
        {
            if (_dropZoneVisual is null)
            {
                _dropZoneVisual = ElementCompositionPreview.GetElementVisual(DropZoneHost);
            }

            _dropZoneVisual.Scale = new Vector3(scaleTarget, scaleTarget, 1f);

            if (DropZoneHighlight is not null)
            {
                DropZoneHighlight.Opacity = highlightTarget;
            }

            if (!isHovering && pulseOnDrop)
            {
                Pulse(DropZoneHost, 1.02);
            }

            return;
        }

        _dropZoneVisual ??= ElementCompositionPreview.GetElementVisual(DropZoneHost);
        _dropZoneHighlightVisual ??= DropZoneHighlight is not null
            ? ElementCompositionPreview.GetElementVisual(DropZoneHighlight)
            : null;

        UpdateDropZoneCenterPoint();

        var token = AnimationToken;
        if (token.IsCancellationRequested)
        {
            return;
        }

        var durationKey = isHovering ? AnimationResourceKeys.Medium : AnimationResourceKeys.Fast;
        var easingKey = isHovering ? AnimationResourceKeys.EaseOut : AnimationResourceKeys.EaseIn;

        var duration = AnimationResourceHelper.GetDuration(durationKey);
        var easing = AnimationResourceHelper.CreateEasing(_compositor, easingKey);

        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        var scaleAnimation = _compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.Duration = duration;
        scaleAnimation.InsertKeyFrame(1f, new Vector3(scaleTarget, scaleTarget, 1f), easing);
        _dropZoneVisual.StartAnimation(nameof(Visual.Scale), scaleAnimation);

        if (_dropZoneHighlightVisual is not null)
        {
            var opacityAnimation = _compositor.CreateScalarKeyFrameAnimation();
            opacityAnimation.Duration = duration;
            opacityAnimation.InsertKeyFrame(1f, highlightTarget, easing);
            _dropZoneHighlightVisual.StartAnimation(nameof(Visual.Opacity), opacityAnimation);
        }
        else if (DropZoneHighlight is not null)
        {
            DropZoneHighlight.Opacity = highlightTarget;
        }

        var tcs = new TaskCompletionSource<object?>();
        batch.Completed += (s, e) =>
        {
            DropZoneHighlight?.SetValue(UIElement.OpacityProperty, (double)highlightTarget);
            tcs.TrySetResult(null);
        };
        batch.End();

        using var registration = token.CanBeCanceled ? token.Register(() => tcs.TrySetCanceled(token)) : default;
        try
        {
            await tcs.Task.ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!isHovering && pulseOnDrop)
        {
            Pulse(DropZoneHost, 1.02);
        }
    }

    private void Pulse(UIElement? target, double peak)
    {
        if (target is not FrameworkElement element || !_animationsEnabled || _compositor is null)
        {
            return;
        }

        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)(element.ActualWidth / 2), (float)(element.ActualHeight / 2), 0f);

        var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Fast);
        var easeOut = AnimationResourceHelper.CreateEasing(_compositor, AnimationResourceKeys.EaseOut);
        var easeIn = AnimationResourceHelper.CreateEasing(_compositor, AnimationResourceKeys.EaseIn);

        var animation = _compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, Vector3.One);
        animation.InsertKeyFrame(0.5f, new Vector3((float)peak, (float)peak, 1f), easeOut);
        animation.InsertKeyFrame(1f, Vector3.One, easeIn);

        visual.StartAnimation(nameof(Visual.Scale), animation);
    }

    private void FadeCloseInfoBar(InfoBar bar)
    {
        FadeOutInfoBar(bar, updateIsOpen: true);
    }

    private void FadeOutInfoBar(InfoBar? bar, bool updateIsOpen)
    {
        if (bar is null)
        {
            return;
        }

        if (!_closingInfoBars.Add(bar))
        {
            return;
        }

        void Complete()
        {
            if (updateIsOpen)
            {
                bar.IsOpen = false;
            }

            bar.IsHitTestVisible = false;
            bar.Visibility = Visibility.Collapsed;
            bar.Opacity = 0;
            _closingInfoBars.Remove(bar);
        }

        if (!_animationsEnabled || _compositor is null)
        {
            Complete();
            return;
        }

        var token = AnimationToken;
        if (token.IsCancellationRequested)
        {
            Complete();
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(bar);
        var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Fast);
        var easing = AnimationResourceHelper.CreateEasing(_compositor, AnimationResourceKeys.EaseIn);

        var animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(1f, 0f, easing);

        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation(nameof(Visual.Opacity), animation);

        batch.Completed += (s, e) =>
        {
            Complete();
        };
        batch.End();
    }

    private void FadeInInfoBar(InfoBar? bar)
    {
        if (bar is null)
        {
            return;
        }

        bar.Visibility = Visibility.Visible;
        bar.IsHitTestVisible = true;
        _closingInfoBars.Remove(bar);

        if (!_animationsEnabled || _compositor is null)
        {
            bar.Opacity = 1;
            return;
        }

        var token = AnimationToken;
        if (token.IsCancellationRequested)
        {
            bar.Opacity = 1;
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(bar);
        visual.Opacity = 0f;

        var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Medium);
        var easing = AnimationResourceHelper.CreateEasing(_compositor, AnimationResourceKeys.EaseOut);

        var animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(1f, 1f, easing);

        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation(nameof(Visual.Opacity), animation);

        batch.Completed += (s, e) =>
        {
            bar.Opacity = 1;
        };
        batch.End();
    }

    private void AttachInfoBarHandlers()
    {
        AttachInfoBar(ActiveStatusInfoBar, ref _activeInfoBarToken);
        AttachInfoBar(DynamicStatusInfoBar, ref _dynamicInfoBarToken);
    }

    private void AttachInfoBar(InfoBar? bar, ref long token)
    {
        if (bar is null)
        {
            return;
        }

        if (token >= 0)
        {
            bar.UnregisterPropertyChangedCallback(InfoBar.IsOpenProperty, token);
        }

        token = bar.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, OnInfoBarIsOpenChanged);

        if (bar.IsOpen)
        {
            FadeInInfoBar(bar);
        }
        else
        {
            bar.Visibility = Visibility.Collapsed;
            bar.Opacity = 0;
            bar.IsHitTestVisible = false;
        }
    }

    private void DetachInfoBarHandlers()
    {
        DetachInfoBar(ActiveStatusInfoBar, ref _activeInfoBarToken);
        DetachInfoBar(DynamicStatusInfoBar, ref _dynamicInfoBarToken);
    }

    private void DetachInfoBar(InfoBar? bar, ref long token)
    {
        if (bar is null || token < 0)
        {
            return;
        }

        bar.UnregisterPropertyChangedCallback(InfoBar.IsOpenProperty, token);
        token = -1;
    }

    private void OnInfoBarIsOpenChanged(DependencyObject sender, DependencyProperty dependencyProperty)
    {
        if (sender is not InfoBar bar)
        {
            return;
        }

        if (bar.IsOpen)
        {
            FadeInInfoBar(bar);
        }
        else
        {
            FadeOutInfoBar(bar, updateIsOpen: false);
        }
    }

    private void OnPageDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null)
        {
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.Caption = "Pustit pro výběr složky";
        }

        SetDragOverlayVisibility(true);
        _ = AnimateDropZoneHoverAsync(true, false);
        e.Handled = true;
    }

    private async void OnPageDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        string? path = null;

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            path = ResolvePathFromStorageItems(items);
        }
        else if (e.DataView.Contains(StandardDataFormats.Text))
        {
            var text = await e.DataView.GetTextAsync();
            path = ExtractPathFromText(text);
        }
        else if (e.DataView.Contains(StandardDataFormats.Uri))
        {
            var uri = await e.DataView.GetUriAsync();
            path = uri?.LocalPath;
        }

        path = NormalizeDroppedPath(path);

        if (!string.IsNullOrWhiteSpace(path))
        {
            if (Directory.Exists(path))
            {
                ViewModel.SelectedFolder = path;
            }
            else if (File.Exists(path))
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    ViewModel.SelectedFolder = directory;
                }
            }
        }

        e.Handled = true;
        SetDragOverlayVisibility(false);
        _ = AnimateDropZoneHoverAsync(false, true);
    }

    private void OnPageDragLeave(object sender, DragEventArgs e)
    {
        SetDragOverlayVisibility(false);
        _ = AnimateDropZoneHoverAsync(false, false);
    }

    private void SetDragOverlayVisibility(bool isVisible)
    {
        if (DragOverlay is null)
        {
            return;
        }

        if (!_animationsEnabled || _compositor is null)
        {
            DragOverlay.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            DragOverlay.Opacity = isVisible ? 1 : 0;
            return;
        }

        var token = AnimationToken;
        if (token.IsCancellationRequested)
        {
            DragOverlay.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            DragOverlay.Opacity = isVisible ? 1 : 0;
            return;
        }

        DragOverlay.Visibility = Visibility.Visible;

        var visual = ElementCompositionPreview.GetElementVisual(DragOverlay);
        var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Panel);
        var easing = AnimationResourceHelper.CreateEasing(_compositor, isVisible ? AnimationResourceKeys.EaseOut : AnimationResourceKeys.EaseIn);

        var animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(1f, isVisible ? 1f : 0f, easing);

        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation(nameof(Visual.Opacity), animation);

        batch.Completed += (s, e) =>
        {
            if (!isVisible)
            {
                DragOverlay.Visibility = Visibility.Collapsed;
            }
        };
        batch.End();
    }

    private void UpdateDropZoneCenterPoint()
    {
        if (DropZoneHost is null || _dropZoneVisual is null)
        {
            return;
        }

        _dropZoneVisual.CenterPoint = new Vector3((float)(DropZoneHost.ActualWidth / 2), (float)(DropZoneHost.ActualHeight / 2), 0f);
    }

    private void OnDropZoneSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDropZoneCenterPoint();
    }

    private static string? ResolvePathFromStorageItems(IReadOnlyList<IStorageItem> items)
    {
        if (items.Count == 0)
        {
            return null;
        }

        var folder = items.OfType<StorageFolder>().FirstOrDefault();
        if (folder is not null)
        {
            return folder.Path;
        }

        var file = items.OfType<StorageFile>().FirstOrDefault();
        return file?.Path;
    }

    private static string? ExtractPathFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var candidate in text
                     .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(static line => line.Trim()))
        {
            if (!string.IsNullOrEmpty(candidate))
            {
                return candidate;
            }
        }

        return text.Trim();
    }

    private static string? NormalizeDroppedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var sanitized = path.Trim();
        if (sanitized.Length >= 2 && sanitized.StartsWith('"') && sanitized.EndsWith('"'))
        {
            sanitized = sanitized[1..^1];
        }

        if (Uri.TryCreate(sanitized, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            sanitized = uri.LocalPath;
        }

        try
        {
            sanitized = Path.GetFullPath(sanitized);
        }
        catch (Exception)
        {
            // Ignore invalid paths and fall back to the original sanitized string.
        }

        sanitized = Path.TrimEndingDirectorySeparator(sanitized);

        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private async void OnEnterAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel?.RunImportCommand.CanExecute(null) == true)
        {
            await ViewModel.RunImportCommand.ExecuteAsync(null);
            args.Handled = true;
        }
    }

    private void OnEscapeAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel?.StopImportCommand.CanExecute(null) == true)
        {
            ViewModel.StopImportCommand.Execute(null);
            args.Handled = true;
        }
    }

    private async void OnOpenAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel?.PickFolderCommand.CanExecute(null) == true)
        {
            await ViewModel.PickFolderCommand.ExecuteAsync(null);
            args.Handled = true;
        }
    }

    private void UpdateQueueAnimations()
    {
        if (ImportQueueRepeater is null)
        {
            return;
        }

        ImplicitListAnimations.Attach(ImportQueueRepeater, _animationsEnabled);
    }

    private void OnInfoBarClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        args.Cancel = true;
        FadeCloseInfoBar(sender);
    }
}
