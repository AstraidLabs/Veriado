// BEGIN CHANGE Veriado.WinUI/App.xaml.cs
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Veriado.WinUI.ViewModels;

namespace Veriado
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Gets the active WinUI window.
        /// </summary>
        public static Window? MainWindowInstance { get; private set; }

        /// <inheritdoc />
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            base.OnLaunched(args);

            AppHost.StartAsync().GetAwaiter().GetResult();

            _window = AppHost.Services.GetRequiredService<MainWindow>();
            var viewModel = AppHost.Services.GetRequiredService<MainWindowViewModel>();
            _window.DataContext = viewModel;
            MainWindowInstance = _window;
            _window.Closed += OnWindowClosed;
            _window.Activate();
        }

        private async void OnWindowClosed(object sender, WindowEventArgs e)
        {
            MainWindowInstance = null;
            await AppHost.StopAsync().ConfigureAwait(false);
        }
    }
}
// END CHANGE Veriado.WinUI/App.xaml.cs
