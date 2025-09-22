using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Veriado.Presentation.Models.Common;

namespace Veriado.Presentation.Models.Files;

public partial class FileGridQueryModel : ObservableObject
{
    [ObservableProperty]
    private string? text;

    [ObservableProperty]
    private bool textPrefix = true;

    [ObservableProperty]
    private bool textAllTerms = true;

    [ObservableProperty]
    private bool fuzzy;

    [ObservableProperty]
    private string? savedQueryKey;

    [ObservableProperty]
    private string? name;

    [ObservableProperty]
    private string? extension;

    [ObservableProperty]
    private string? mime;

    [ObservableProperty]
    private string? author;

    [ObservableProperty]
    private bool? isReadOnly;

    [ObservableProperty]
    private bool? isIndexStale;

    [ObservableProperty]
    private bool? hasValidity;

    [ObservableProperty]
    private bool? isCurrentlyValid;

    [ObservableProperty]
    private int? expiringInDays;

    [ObservableProperty]
    private long? sizeMin;

    [ObservableProperty]
    private long? sizeMax;

    [ObservableProperty]
    private DateTimeOffset? createdFromUtc;

    [ObservableProperty]
    private DateTimeOffset? createdToUtc;

    [ObservableProperty]
    private DateTimeOffset? modifiedFromUtc;

    [ObservableProperty]
    private DateTimeOffset? modifiedToUtc;

    [ObservableProperty]
    private ObservableCollection<FileSortSpecModel> sort = new();

    [ObservableProperty]
    private PageRequestModel page = new();
}
