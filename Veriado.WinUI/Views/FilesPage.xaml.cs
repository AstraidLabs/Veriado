using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Veriado.Presentation.ViewModels;

namespace Veriado.WinUI.Views;

public sealed partial class FilesPage : Page
{
    public FilesPage()
    {
        InitializeComponent();
        ViewModel = AppHost.Services.GetRequiredService<FilesGridViewModel>();
    }

    public FilesGridViewModel ViewModel { get; }
}
