using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Files;

public partial class FileSummaryModel : ObservableObject
{
    [ObservableProperty]
    private Guid id;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string extension = string.Empty;

    [ObservableProperty]
    private string mime = string.Empty;

    [ObservableProperty]
    private string author = string.Empty;

    [ObservableProperty]
    private long size;

    [ObservableProperty]
    private DateTimeOffset createdUtc;

    [ObservableProperty]
    private DateTimeOffset lastModifiedUtc;

    [ObservableProperty]
    private bool isReadOnly;

    [ObservableProperty]
    private int version;

    [ObservableProperty]
    private FileValidityModel? validity;

    [ObservableProperty]
    private bool isIndexStale;

    [ObservableProperty]
    private DateTimeOffset? lastIndexedUtc;

    [ObservableProperty]
    private string? indexedTitle;

    [ObservableProperty]
    private int indexSchemaVersion;

    [ObservableProperty]
    private string? indexedContentHash;

    [ObservableProperty]
    private double? score;

    public bool HasValidity => Validity is not null;

    public bool IsCurrentlyValid =>
        Validity is { IssuedAt: var issued, ValidUntil: var until }
        && issued <= DateTimeOffset.UtcNow
        && until >= DateTimeOffset.UtcNow;

    public bool IsExpired =>
        Validity is { ValidUntil: var until }
        && until < DateTimeOffset.UtcNow;
}
