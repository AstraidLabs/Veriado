using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Common;

public partial class PageResultModel<T> : ObservableObject
{
    [ObservableProperty]
    private IReadOnlyList<T> items = Array.Empty<T>();

    [ObservableProperty]
    private int page;

    [ObservableProperty]
    private int pageSize;

    [ObservableProperty]
    private int totalCount;

    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
