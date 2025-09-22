using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Files;

public partial class FileSortSpecModel : ObservableObject
{
    [ObservableProperty]
    private string field = string.Empty;

    [ObservableProperty]
    private bool descending;
}
