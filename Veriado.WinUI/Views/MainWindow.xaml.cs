using System;
using Microsoft.UI.Xaml;
using Veriado.WinUI.ViewModels;

namespace Veriado.Views
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow(ShellViewModel viewModel)
        {
            InitializeComponent();

            ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            LayoutRoot.DataContext = ViewModel;
        }

        public ShellViewModel ViewModel { get; }
    }
}
