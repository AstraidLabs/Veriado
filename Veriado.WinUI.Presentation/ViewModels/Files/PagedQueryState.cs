using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.ViewModels.Files;

/// <summary>
/// Captures paging information for the incremental loading collection.
/// </summary>
public sealed partial class PagedQueryState : ObservableObject
{
    [ObservableProperty]
    private int pageSize = 50;

    [ObservableProperty]
    private int totalCount;
}
