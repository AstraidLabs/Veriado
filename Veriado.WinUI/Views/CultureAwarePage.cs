using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Views;

public abstract class CultureAwarePage : Page
{
    private ILocalizationService? _localizationService;

    protected CultureAwarePage()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected virtual void OnCultureChanged(CultureInfo culture)
    {
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachLocalizationService();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_localizationService is null)
        {
            return;
        }

        _localizationService.CultureChanged -= OnLocalizationServiceCultureChanged;
    }

    private void AttachLocalizationService()
    {
        if (_localizationService is null)
        {
            try
            {
                _localizationService = App.Services.GetService<ILocalizationService>();
            }
            catch
            {
                _localizationService = null;
            }
        }

        if (_localizationService is not null)
        {
            _localizationService.CultureChanged -= OnLocalizationServiceCultureChanged;
            _localizationService.CultureChanged += OnLocalizationServiceCultureChanged;
        }
    }

    private void OnLocalizationServiceCultureChanged(object? sender, CultureInfo culture)
    {
        if (DispatcherQueue is null)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() => HandleCultureChanged(culture));
    }

    private void HandleCultureChanged(CultureInfo culture)
    {
        if (Frame?.Content is not Page currentPage || !ReferenceEquals(currentPage, this))
        {
            return;
        }

        OnCultureChanged(culture);

        var transition = new SuppressNavigationTransitionInfo();
        Frame.Navigate(GetType(), null, transition);
    }
}
