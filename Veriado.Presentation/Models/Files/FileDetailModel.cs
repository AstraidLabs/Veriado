using System;
using CommunityToolkit.Mvvm.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Files;

public partial class FileDetailModel : ObservableObject
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
    private FileContentModel content = new();

    [ObservableProperty]
    private FileSystemMetadataModel systemMetadata = new();

    [ObservableProperty]
    private ObservableDictionary<string, string?> extendedMetadata = new();

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

    public FileSummaryModel CreateSummary()
    {
        var summary = new FileSummaryModel
        {
            Id = Id,
            Name = Name,
            Extension = Extension,
            Mime = Mime,
            Author = Author,
            Size = Size,
            CreatedUtc = CreatedUtc,
            LastModifiedUtc = LastModifiedUtc,
            IsReadOnly = IsReadOnly,
            Version = Version,
            Validity = Validity,
            IsIndexStale = IsIndexStale,
            LastIndexedUtc = LastIndexedUtc,
            IndexedTitle = IndexedTitle,
            IndexSchemaVersion = IndexSchemaVersion,
            IndexedContentHash = IndexedContentHash,
            Score = null,
        };

        return summary;
    }
}
