using System;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Import;

namespace Veriado.WinUI.Views;

public sealed partial class ImportView : UserControl
{
    public ImportView(ImportViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public ImportViewModel ViewModel => (ImportViewModel)DataContext!;
}
