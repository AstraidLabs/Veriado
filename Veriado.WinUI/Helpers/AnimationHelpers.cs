using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.Win32;

namespace Veriado.WinUI.Helpers;

internal static class AnimationSettings
{
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    private static bool _areAnimationsEnabled = true;

    static AnimationSettings()
    {
        try
        {
            _areAnimationsEnabled = GetClientAreaAnimationPreference();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }
        catch
        {
            _areAnimationsEnabled = true;
        }
    }

    public static bool AreEnabled => _areAnimationsEnabled;

    public static event EventHandler<bool>? AnimationsEnabledChanged;

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            UpdateAnimationPreference();
        }
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }

    private static void UpdateAnimationPreference()
    {
        var enabled = GetClientAreaAnimationPreference();

        if (_areAnimationsEnabled != enabled)
        {
            _areAnimationsEnabled = enabled;
            AnimationsEnabledChanged?.Invoke(null, enabled);
        }
    }

    private static bool GetClientAreaAnimationPreference()
    {
        return SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out bool enabled, 0) ? enabled : true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out bool pvParam, uint fWinIni);
}

internal static class AnimationResourceKeys
{
    public const string Fast = "Anim.Fast";
    public const string Medium = "Anim.Med";
    public const string Panel = "Anim.Panel";
    public const string EaseOut = "Ease.Out";
    public const string EaseIn = "Ease.In";
}

internal static class AnimationResourceHelper
{
    public static TimeSpan GetDuration(string key)
    {
        if (Application.Current?.Resources.ContainsKey(key) == true && Application.Current.Resources[key] is TimeSpan timeSpan)
        {
            return timeSpan;
        }

        return TimeSpan.FromMilliseconds(150);
    }

    public static CompositionEasingFunction CreateEasing(Compositor compositor, string key)
    {
        return key switch
        {
            AnimationResourceKeys.EaseOut => compositor.CreateCubicBezierEasingFunction(new Vector2(0.17f, 0.17f), new Vector2(0f, 1f)),
            AnimationResourceKeys.EaseIn => compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f)),
            _ => compositor.CreateLinearEasingFunction(),
        };
    }
}

public static class PulseButtonHelper
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(PulseButtonHelper),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            button.Click += OnButtonClick;
            button.Unloaded += OnButtonUnloaded;
        }
        else
        {
            button.Click -= OnButtonClick;
            button.Unloaded -= OnButtonUnloaded;
        }
    }

    private static void OnButtonUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.Click -= OnButtonClick;
            button.Unloaded -= OnButtonUnloaded;
        }
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (!AnimationSettings.AreEnabled || sender is not Button button)
        {
            return;
        }

        if (button.ActualWidth <= 0 || button.ActualHeight <= 0)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(button);
        var compositor = visual.Compositor;
        var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Fast);
        var easing = AnimationResourceHelper.CreateEasing(compositor, AnimationResourceKeys.EaseOut);

        visual.CenterPoint = new Vector3((float)(button.ActualWidth / 2), (float)(button.ActualHeight / 2), 0f);

        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Target = nameof(Visual.Scale);
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
        animation.InsertKeyFrame(0.5f, new Vector3(1.05f, 1.05f, 1f), easing);
        animation.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), easing);

        visual.StartAnimation(nameof(Visual.Scale), animation);
    }
}

public static class ExpanderAnimationHelper
{
    private static readonly DependencyProperty IsCollapsingInternallyProperty = DependencyProperty.RegisterAttached(
        "IsCollapsingInternally",
        typeof(bool),
        typeof(ExpanderAnimationHelper),
        new PropertyMetadata(false));

    private static readonly DependencyProperty PropertyChangedCallbackTokenProperty = DependencyProperty.RegisterAttached(
        "PropertyChangedCallbackToken",
        typeof(long),
        typeof(ExpanderAnimationHelper),
        new PropertyMetadata(-1L));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ExpanderAnimationHelper),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static bool GetIsCollapsingInternally(DependencyObject element) => (bool)element.GetValue(IsCollapsingInternallyProperty);

    private static void SetIsCollapsingInternally(DependencyObject element, bool value) => element.SetValue(IsCollapsingInternallyProperty, value);

    private static long GetPropertyChangedCallbackToken(DependencyObject element) => (long)element.GetValue(PropertyChangedCallbackTokenProperty);

    private static void SetPropertyChangedCallbackToken(DependencyObject element, long value) => element.SetValue(PropertyChangedCallbackTokenProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Expander expander)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            expander.Loaded += OnExpanderLoaded;
            expander.Unloaded += OnExpanderUnloaded;
            if (expander.IsLoaded)
            {
                Attach(expander);
            }
        }
        else
        {
            expander.Loaded -= OnExpanderLoaded;
            expander.Unloaded -= OnExpanderUnloaded;
            Detach(expander);
        }
    }

    private static void OnExpanderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander)
        {
            Attach(expander);
        }
    }

    private static void OnExpanderUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander)
        {
            Detach(expander);
        }
    }

    private static void Attach(Expander expander)
    {
        expander.Expanding += OnExpanderExpanding;
        expander.Collapsed += OnExpanderCollapsed;

        var token = expander.RegisterPropertyChangedCallback(Expander.IsExpandedProperty, OnExpanderIsExpandedChanged);
        SetPropertyChangedCallbackToken(expander, token);

        if (TryGetContent(expander, out var content))
        {
            content.Opacity = expander.IsExpanded ? 1d : 0d;
        }
    }

    private static void Detach(Expander expander)
    {
        expander.Expanding -= OnExpanderExpanding;
        expander.Collapsed -= OnExpanderCollapsed;

        var token = GetPropertyChangedCallbackToken(expander);
        if (token >= 0)
        {
            expander.UnregisterPropertyChangedCallback(Expander.IsExpandedProperty, token);
            SetPropertyChangedCallbackToken(expander, -1);
        }
    }

    private static void OnExpanderExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (!TryGetContent(sender, out var content))
        {
            return;
        }

        if (!AnimationSettings.AreEnabled)
        {
            content.Opacity = 1d;
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(content);
        var compositor = visual.Compositor;
        var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Panel);
        var easing = AnimationResourceHelper.CreateEasing(compositor, AnimationResourceKeys.EaseOut);

        EnsureCenterPoint(visual, content, sender);

        visual.Opacity = 0f;
        visual.Scale = new Vector3(1f, 0.92f, 1f);

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.Target = nameof(Visual.Scale);
        scaleAnimation.Duration = duration;
        scaleAnimation.InsertKeyFrame(0f, new Vector3(1f, 0.92f, 1f));
        scaleAnimation.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), easing);

        var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.Target = nameof(Visual.Opacity);
        opacityAnimation.Duration = duration;
        opacityAnimation.InsertKeyFrame(0f, 0f);
        opacityAnimation.InsertKeyFrame(1f, 1f, easing);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation(nameof(Visual.Scale), scaleAnimation);
        visual.StartAnimation(nameof(Visual.Opacity), opacityAnimation);
        batch.Completed += (_, __) => content.Opacity = 1d;
        batch.End();
    }

    private static void OnExpanderIsExpandedChanged(DependencyObject sender, DependencyProperty dependencyProperty)
    {
        if (sender is not Expander expander || dependencyProperty != Expander.IsExpandedProperty)
        {
            return;
        }

        if (!TryGetContent(expander, out var content))
        {
            return;
        }

        if (expander.IsExpanded)
        {
            if (!AnimationSettings.AreEnabled)
            {
                content.Opacity = 1d;
            }

            return;
        }

        HandleCollapse(expander, content);
    }

    private static bool TryGetContent(Expander expander, out FrameworkElement content)
    {
        if (expander.Content is FrameworkElement frameworkElement)
        {
            content = frameworkElement;
            return true;
        }

        content = null!;
        return false;
    }

    private static void HandleCollapse(Expander expander, FrameworkElement content)
    {
        if (GetIsCollapsingInternally(expander))
        {
            return;
        }

        if (!AnimationSettings.AreEnabled)
        {
            content.Opacity = 0d;
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(content);
        var compositor = visual.Compositor;
        var duration = AnimationResourceHelper.GetDuration(AnimationResourceKeys.Panel);
        var easing = AnimationResourceHelper.CreateEasing(compositor, AnimationResourceKeys.EaseOut);

        EnsureCenterPoint(visual, content, expander);

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.Target = nameof(Visual.Scale);
        scaleAnimation.Duration = duration;
        scaleAnimation.InsertKeyFrame(0f, visual.Scale);
        scaleAnimation.InsertKeyFrame(1f, new Vector3(1f, 0.92f, 1f), easing);

        var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.Target = nameof(Visual.Opacity);
        opacityAnimation.Duration = duration;
        opacityAnimation.InsertKeyFrame(0f, visual.Opacity);
        opacityAnimation.InsertKeyFrame(1f, 0f, easing);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, __) =>
        {
            content.Opacity = 0d;
            _ = expander.DispatcherQueue.TryEnqueue(() => expander.IsExpanded = false);
        };
        visual.StartAnimation(nameof(Visual.Scale), scaleAnimation);
        visual.StartAnimation(nameof(Visual.Opacity), opacityAnimation);
        batch.End();

        SetIsCollapsingInternally(expander, true);
        expander.IsExpanded = true;
    }

    private static void OnExpanderCollapsed(Expander sender, ExpanderCollapsedEventArgs args)
    {
        if (GetIsCollapsingInternally(sender))
        {
            SetIsCollapsingInternally(sender, false);
        }

        if (TryGetContent(sender, out var content))
        {
            if (!AnimationSettings.AreEnabled)
            {
                content.Opacity = 0d;
            }
        }
    }

    private static void EnsureCenterPoint(Visual visual, FrameworkElement content, FrameworkElement expander)
    {
        var width = content.RenderSize.Width > 0 ? content.RenderSize.Width : expander.ActualWidth;
        visual.CenterPoint = new Vector3((float)(width / 2), 0f, 0f);
    }
}
