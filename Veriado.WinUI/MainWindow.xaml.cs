// BEGIN CHANGE Veriado.WinUI/MainWindow.xaml.cs
using System;
using Microsoft.UI.Xaml;
using Veriado.WinUI.ViewModels;

namespace Veriado
{
    /// <summary>
    /// Main application window providing the composite shell.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = ViewModel;
            Title = "Veriado";
            Loaded += OnLoaded;
        }

        public MainViewModel ViewModel { get; }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await ViewModel.InitializeAsync().ConfigureAwait(false);
        }
    }
}
// END CHANGE Veriado.WinUI/MainWindow.xaml.cs
