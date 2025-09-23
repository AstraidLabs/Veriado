using System;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views;

public sealed partial class FileDetailView : UserControl
{
    public FileDetailView(FileDetailViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public FileDetailViewModel ViewModel => (FileDetailViewModel)DataContext!;
}
