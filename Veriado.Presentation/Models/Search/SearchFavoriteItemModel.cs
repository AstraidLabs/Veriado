using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Search;

public partial class SearchFavoriteItemModel : ObservableObject
{
    [ObservableProperty]
    private Guid id;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? queryText;

    [ObservableProperty]
    private string matchQuery = string.Empty;

    [ObservableProperty]
    private int position;

    [ObservableProperty]
    private DateTimeOffset createdUtc;

    [ObservableProperty]
    private bool isFuzzy;
}
