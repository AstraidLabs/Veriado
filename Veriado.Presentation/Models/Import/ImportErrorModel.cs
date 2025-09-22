using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Import;

public partial class ImportErrorModel : ObservableObject
{
    [ObservableProperty]
    private string filePath = string.Empty;

    [ObservableProperty]
    private string code = string.Empty;

    [ObservableProperty]
    private string message = string.Empty;
}
