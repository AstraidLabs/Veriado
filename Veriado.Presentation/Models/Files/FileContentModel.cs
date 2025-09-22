using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Files;

public partial class FileContentModel : ObservableObject
{
    [ObservableProperty]
    private string hash = string.Empty;

    [ObservableProperty]
    private long length;

    [ObservableProperty]
    private byte[]? bytes;

    public bool HasBytes => Bytes is { Length: > 0 };

    public ReadOnlyMemory<byte> AsMemory() => Bytes is { Length: > 0 } value ? new ReadOnlyMemory<byte>(value) : ReadOnlyMemory<byte>.Empty;
}
