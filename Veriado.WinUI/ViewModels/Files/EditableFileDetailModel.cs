using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using Veriado.Application.Files.Contracts;

namespace Veriado.WinUI.ViewModels.Files;

/// <summary>
/// Represents an editable snapshot of a file detail with validation support for the dialog.
/// </summary>
public sealed partial class EditableFileDetailModel : ObservableValidator
{
    private static readonly string[] ValidatedMembers =
    {
        nameof(FileName),
        nameof(MimeType),
        nameof(Author),
        nameof(ValidFrom),
        nameof(ValidTo),
        string.Empty,
    };

    private FileDetailDto _snapshot = null!;

    private EditableFileDetailModel()
    {
    }

    public Guid Id { get; private set; }

    public string Extension { get; private set; } = string.Empty;

    [ObservableProperty]
    [Required(ErrorMessage = "Název souboru je povinný.")]
    [StringLength(255, MinimumLength = 1, ErrorMessage = "Název musí mít 1 až 255 znaků.")]
    private string fileName = string.Empty;

    [ObservableProperty]
    [Required(ErrorMessage = "MIME typ je povinný.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "MIME typ nesmí být delší než 200 znaků.")]
    private string mimeType = string.Empty;

    [ObservableProperty]
    [StringLength(200, ErrorMessage = "Autor nesmí být delší než 200 znaků.")]
    private string? author;

    [ObservableProperty]
    private bool isReadOnly;

    [ObservableProperty]
    private long size;

    [ObservableProperty]
    private DateTimeOffset createdAt;

    [ObservableProperty]
    private DateTimeOffset modifiedAt;

    [ObservableProperty]
    private int version;

    [ObservableProperty]
    private DateTimeOffset? validFrom;

    [ObservableProperty]
    private DateTimeOffset? validTo;

    public string DisplayName => _snapshot.DisplayName;

    public bool HasValidity => ValidFrom is not null && ValidTo is not null;

    public bool IsDirty
        => !string.Equals(FileName, _snapshot.FileName, StringComparison.Ordinal)
            || !string.Equals(MimeType, _snapshot.MimeType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Author, _snapshot.Author, StringComparison.Ordinal)
            || IsReadOnly != _snapshot.IsReadOnly
            || !Nullable.Equals(ValidFrom, _snapshot.ValidFrom)
            || !Nullable.Equals(ValidTo, _snapshot.ValidTo);

    public static EditableFileDetailModel FromDto(FileDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var model = new EditableFileDetailModel
        {
            _snapshot = detail,
            Id = detail.Id,
            Extension = detail.Extension,
            fileName = detail.FileName,
            mimeType = detail.MimeType,
            author = detail.Author,
            isReadOnly = detail.IsReadOnly,
            size = detail.Size,
            createdAt = detail.CreatedAt,
            modifiedAt = detail.ModifiedAt,
            version = detail.Version,
            validFrom = detail.ValidFrom,
            validTo = detail.ValidTo,
        };

        model.ResetValidation();
        return model;
    }

    public FileDetailDto ToDto()
    {
        return new FileDetailDto
        {
            Id = Id,
            FileName = FileName,
            Extension = Extension,
            MimeType = MimeType,
            Author = Author,
            IsReadOnly = IsReadOnly,
            Size = Size,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
            Version = Version,
            ValidFrom = ValidFrom,
            ValidTo = ValidTo,
        };
    }

    public void UpdateSnapshot(FileDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        _snapshot = detail;
        Version = detail.Version;
        ModifiedAt = detail.ModifiedAt;
    }

    public void ApplyServerErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ResetValidation();

        foreach (var pair in errors)
        {
            SetErrors(pair.Key ?? string.Empty, pair.Value);
        }
    }

    public void ResetValidation()
    {
        foreach (var member in ValidatedMembers)
        {
            SetErrors(member, Array.Empty<string>());
        }
    }

    public void ValidateAll()
    {
        ValidateAllProperties();
        ValidateValidityRange();
    }

    partial void OnFileNameChanged(string value)
    {
        ValidateProperty(value, nameof(FileName));
    }

    partial void OnMimeTypeChanged(string value)
    {
        ValidateProperty(value, nameof(MimeType));
    }

    partial void OnAuthorChanged(string? value)
    {
        ValidateProperty(value, nameof(Author));
    }

    partial void OnValidFromChanged(DateTimeOffset? value)
    {
        ValidateValidityRange();
    }

    partial void OnValidToChanged(DateTimeOffset? value)
    {
        ValidateValidityRange();
    }

    private void ValidateValidityRange()
    {
        if (ValidFrom is null && ValidTo is null)
        {
            SetErrors(nameof(ValidFrom), Array.Empty<string>());
            SetErrors(nameof(ValidTo), Array.Empty<string>());
            return;
        }

        if (ValidFrom is null || ValidTo is null)
        {
            SetErrors(nameof(ValidFrom), ValidFrom is null ? new[] { "Datum začátku platnosti je povinné." } : Array.Empty<string>());
            SetErrors(nameof(ValidTo), ValidTo is null ? new[] { "Datum ukončení platnosti je povinné." } : Array.Empty<string>());
            return;
        }

        if (ValidFrom > ValidTo)
        {
            SetErrors(nameof(ValidFrom), new[] { "Začátek platnosti musí být dříve než konec." });
            SetErrors(nameof(ValidTo), new[] { "Konec platnosti musí být po začátku." });
            return;
        }

        SetErrors(nameof(ValidFrom), Array.Empty<string>());
        SetErrors(nameof(ValidTo), Array.Empty<string>());
    }
}
