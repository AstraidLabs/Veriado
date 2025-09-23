using System;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views;

public sealed partial class FilesView : UserControl
{
    public FilesView(FilesGridViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public FilesGridViewModel ViewModel => (FilesGridViewModel)DataContext!;
}
