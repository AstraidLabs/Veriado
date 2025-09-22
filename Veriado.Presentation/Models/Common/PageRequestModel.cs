using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Common;

public partial class PageRequestModel : ObservableObject
{
    [ObservableProperty]
    private int page = 1;

    [ObservableProperty]
    private int pageSize = 50;
}
