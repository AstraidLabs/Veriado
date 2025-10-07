using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.ViewModels.Files;

namespace Veriado.WinUI.Views.Files;

public sealed partial class FileDetailView : UserControl
{
    public FileDetailView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public FileDetailViewModel? ViewModel => DataContext as FileDetailViewModel;

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        Bindings.Update();
    }
}
