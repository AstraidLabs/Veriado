using System;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Settings;

namespace Veriado.WinUI.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;
}
