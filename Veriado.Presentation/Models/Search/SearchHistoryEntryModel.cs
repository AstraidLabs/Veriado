using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Search;

public partial class SearchHistoryEntryModel : ObservableObject
{
    [ObservableProperty]
    private Guid id;

    [ObservableProperty]
    private string? queryText;

    [ObservableProperty]
    private string matchQuery = string.Empty;

    [ObservableProperty]
    private DateTimeOffset lastQueriedUtc;

    [ObservableProperty]
    private int executions;

    [ObservableProperty]
    private int? lastTotalHits;

    [ObservableProperty]
    private bool isFuzzy;

    public bool HasQueryText => !string.IsNullOrWhiteSpace(QueryText);
}
