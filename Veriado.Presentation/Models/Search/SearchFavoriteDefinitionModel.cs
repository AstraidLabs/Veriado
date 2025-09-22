using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Search;

public partial class SearchFavoriteDefinitionModel : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string matchQuery = string.Empty;

    [ObservableProperty]
    private string? queryText;

    [ObservableProperty]
    private bool isFuzzy;
}
