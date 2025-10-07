using System;
using System.Globalization;
using Veriado.Contracts.Files;
using Veriado.WinUI.Converters;

namespace Veriado.WinUI.ViewModels.Files;

public sealed class FileDetailViewModel
{
    private static readonly SizeToHumanConverter SizeConverter = new();
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("cs-CZ");

    public FileDetailViewModel(FileSummaryDto file)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
    }

    public FileSummaryDto File { get; }

    public Guid Id => File.Id;

    public string Name => File.Name;

    public string? Title => string.IsNullOrWhiteSpace(File.Title) ? null : File.Title;

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    public string Extension => File.Extension;

    public string Mime => File.Mime;

    public string Author => File.Author;

    public string SizeText => (string)SizeConverter.Convert(File.Size, typeof(string), null, string.Empty);

    public int Version => File.Version;

    public string ReadOnlyText => File.IsReadOnly ? "Ano" : "Ne";

    public string CreatedText => FormatDateTime(File.CreatedUtc);

    public string LastModifiedText => FormatDateTime(File.LastModifiedUtc);

    public bool HasValidity => File.Validity is not null;

    public string IssuedAtText => File.Validity is { } validity ? FormatDateTime(validity.IssuedAt) : string.Empty;

    public string ValidUntilText => File.Validity is { } validity ? FormatDateTime(validity.ValidUntil) : string.Empty;

    public string PhysicalCopyText => File.Validity?.HasPhysicalCopy == true ? "Ano" : "Ne";

    public string ElectronicCopyText => File.Validity?.HasElectronicCopy == true ? "Ano" : "Ne";

    public string DialogTitle => string.IsNullOrWhiteSpace(Name)
        ? "Detail souboru"
        : $"Detail souboru – {Name}";

    private static string FormatDateTime(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Culture);
    }
}
