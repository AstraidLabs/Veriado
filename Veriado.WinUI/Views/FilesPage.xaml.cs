using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Veriado.ViewModels.Files;

namespace Veriado.Views;

public sealed partial class FilesPage : Page
{
    public FilesGridViewModel ViewModel { get; }

    public FilesPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<FilesGridViewModel>();
        DataContext = ViewModel;
    }
}
