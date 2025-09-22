using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Import;

public partial class ImportFolderRequestModel : ObservableObject
{
    [ObservableProperty]
    private string folderPath = string.Empty;

    [ObservableProperty]
    private string? defaultAuthor;

    [ObservableProperty]
    private bool extractContent = true;

    [ObservableProperty]
    private int maxDegreeOfParallelism = 4;

    [ObservableProperty]
    private string? searchPattern = "*";

    [ObservableProperty]
    private bool recursive = true;
}
