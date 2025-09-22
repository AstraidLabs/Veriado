using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Import;

public partial class ImportProgressModel : ObservableObject
{
    [ObservableProperty]
    private int processed;

    [ObservableProperty]
    private int total;

    [ObservableProperty]
    private string? lastError;

    [ObservableProperty]
    private string? currentFile;

    public double Percent => Total == 0 ? 0 : (double)Processed / Total * 100.0;
}
