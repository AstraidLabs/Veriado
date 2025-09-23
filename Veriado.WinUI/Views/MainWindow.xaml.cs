using System;
using Microsoft.UI.Xaml;
using Veriado.WinUI.ViewModels;

namespace Veriado.Views
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow(ShellViewModel viewModel)
        {
            this.InitializeComponent(); // (this. je volitelné)
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        public ShellViewModel ViewModel => (ShellViewModel)DataContext!;
    }
}
