using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Files;

public partial class FileSystemMetadataModel : ObservableObject
{
    [ObservableProperty]
    private int attributes;

    [ObservableProperty]
    private DateTimeOffset createdUtc;

    [ObservableProperty]
    private DateTimeOffset lastWriteUtc;

    [ObservableProperty]
    private DateTimeOffset lastAccessUtc;

    [ObservableProperty]
    private string? ownerSid;

    [ObservableProperty]
    private uint? hardLinkCount;

    [ObservableProperty]
    private uint? alternateDataStreamCount;
}
