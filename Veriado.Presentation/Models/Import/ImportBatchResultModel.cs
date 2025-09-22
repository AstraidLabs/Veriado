using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Import;

public partial class ImportBatchResultModel : ObservableObject
{
    [ObservableProperty]
    private int total;

    [ObservableProperty]
    private int succeeded;

    [ObservableProperty]
    private int failed;

    [ObservableProperty]
    private IReadOnlyList<ImportErrorModel> errors = Array.Empty<ImportErrorModel>();
}
