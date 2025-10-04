using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
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

public static class NavigationAnimations
{
    public static void Attach(Frame frame, bool enabled)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (!enabled)
        {
            frame.ContentTransitions = null;
            return;
        }

        var transitions = new TransitionCollection
        {
            new NavigationThemeTransition
            {
                DefaultNavigationTransitionInfo = new EntranceNavigationTransitionInfo()
            }
        };

        frame.ContentTransitions = transitions;
    }
}

public static class ImplicitListAnimations
{
    private sealed class ItemsRepeaterAnimationConfig
    {
        public bool IsEnabled { get; set; }

        public ImplicitAnimationCollection? Animations { get; set; }

        public float Offset { get; set; }

        public TimeSpan Duration { get; set; }
    }

    private static readonly DependencyProperty ItemsRepeaterConfigProperty = DependencyProperty.RegisterAttached(
        "ItemsRepeaterConfig",
        typeof(ItemsRepeaterAnimationConfig),
        typeof(ImplicitListAnimations),
        new PropertyMetadata(null));

    public static void Attach(FrameworkElement host, bool enabled, float offset = 12f, int ms = 160)
    {
        if (host is null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        var clampedDuration = TimeSpan.FromMilliseconds(Math.Clamp(ms, 120, 200));

        if (host is ItemsRepeater repeater)
        {
            AttachToItemsRepeater(repeater, enabled, offset, clampedDuration);
            return;
        }

        ApplyImplicitAnimations(host, enabled, offset, clampedDuration);
    }

    private static void AttachToItemsRepeater(ItemsRepeater repeater, bool enabled, float offset, TimeSpan duration)
    {
        if (repeater.GetValue(ItemsRepeaterConfigProperty) is not ItemsRepeaterAnimationConfig config)
        {
            config = new ItemsRepeaterAnimationConfig();
            repeater.SetValue(ItemsRepeaterConfigProperty, config);
            repeater.ElementPrepared += OnItemsRepeaterElementPrepared;
            repeater.ElementClearing += OnItemsRepeaterElementClearing;
        }

        config.IsEnabled = enabled && AnimationSettings.AreEnabled;
        config.Offset = offset;
        config.Duration = duration;

        if (!config.IsEnabled)
        {
            config.Animations = null;
            ResetRealizedElements(repeater);
        }
        else
        {
            EnsureRealizedElementsInitialized(repeater, config);
        }
    }

    private static void ApplyImplicitAnimations(FrameworkElement element, bool enabled, float offset, TimeSpan duration)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);

        if (!enabled || !AnimationSettings.AreEnabled)
        {
            visual.ImplicitAnimations = null;
            visual.Offset = Vector3.Zero;
            visual.Opacity = 1f;
            return;
        }

        visual.ImplicitAnimations = CreateImplicitAnimations(visual.Compositor, offset, duration);
    }

    private static void OnItemsRepeaterElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not UIElement element)
        {
            return;
        }

        if (sender.GetValue(ItemsRepeaterConfigProperty) is not ItemsRepeaterAnimationConfig config)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);

        if (!config.IsEnabled)
        {
            visual.ImplicitAnimations = null;
            visual.Opacity = 1f;
            visual.Offset = Vector3.Zero;
            return;
        }

        config.Animations ??= CreateImplicitAnimations(visual.Compositor, config.Offset, config.Duration);

        visual.ImplicitAnimations = config.Animations;
        visual.Opacity = 0f;
        visual.Offset = new Vector3(0f, config.Offset, 0f);
        visual.Opacity = 1f;
        visual.Offset = Vector3.Zero;
    }

    private static void OnItemsRepeaterElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is not UIElement element)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.ImplicitAnimations = null;
        visual.Opacity = 1f;
        visual.Offset = Vector3.Zero;
    }

    private static ImplicitAnimationCollection CreateImplicitAnimations(Compositor compositor, float offset, TimeSpan duration)
    {
        var easing = AnimationResourceHelper.CreateEasing(compositor, AnimationResourceKeys.EaseOut);

        var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
        fadeAnimation.Target = nameof(Visual.Opacity);
        fadeAnimation.Duration = duration;
        fadeAnimation.InsertKeyFrame(0f, 0f);
        fadeAnimation.InsertKeyFrame(1f, 1f, easing);

        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.Target = nameof(Visual.Offset);
        offsetAnimation.Duration = duration;
        offsetAnimation.InsertKeyFrame(0f, new Vector3(0f, offset, 0f));
        offsetAnimation.InsertKeyFrame(1f, Vector3.Zero, easing);

        var animations = compositor.CreateImplicitAnimationCollection();
        animations[nameof(Visual.Opacity)] = fadeAnimation;
        animations[nameof(Visual.Offset)] = offsetAnimation;

        return animations;
    }

    private static void ResetRealizedElements(ItemsRepeater repeater)
    {
        if (repeater.ItemsSourceView is null)
        {
            return;
        }

        var count = repeater.ItemsSourceView.Count;
        for (var i = 0; i < count; i++)
        {
            if (repeater.TryGetElement(i) is UIElement element)
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                visual.ImplicitAnimations = null;
                visual.Opacity = 1f;
                visual.Offset = Vector3.Zero;
            }
        }
    }

    private static void EnsureRealizedElementsInitialized(ItemsRepeater repeater, ItemsRepeaterAnimationConfig config)
    {
        if (repeater.ItemsSourceView is null)
        {
            return;
        }

        var count = repeater.ItemsSourceView.Count;
        for (var i = 0; i < count; i++)
        {
            if (repeater.TryGetElement(i) is UIElement element)
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                config.Animations ??= CreateImplicitAnimations(visual.Compositor, config.Offset, config.Duration);
                visual.ImplicitAnimations = config.Animations;
                visual.Opacity = 1f;
                visual.Offset = Vector3.Zero;
            }
        }
    }
}
