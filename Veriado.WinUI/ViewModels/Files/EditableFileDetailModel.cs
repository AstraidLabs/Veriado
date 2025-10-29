using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Veriado.Appl.Files.Contracts;

namespace Veriado.WinUI.ViewModels.Files;

/// <summary>
/// Represents an editable snapshot of a file detail with validation support for the dialog.
/// </summary>
public sealed partial class EditableFileDetailModel : ObservableValidator, INotifyDataErrorInfo
{
    private EditableFileDetailDto _snapshot = null!;
    private readonly Dictionary<string, string[]> _externalErrors = new(StringComparer.Ordinal);

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

    public new bool HasErrors => base.HasErrors || _externalErrors.Count > 0;

    bool INotifyDataErrorInfo.HasErrors => HasErrors;

    public bool IsDirty
        => !string.Equals(FileName, _snapshot.FileName, StringComparison.Ordinal)
            || !string.Equals(MimeType, _snapshot.MimeType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Author, _snapshot.Author, StringComparison.Ordinal)
            || IsReadOnly != _snapshot.IsReadOnly
            || !Nullable.Equals(ValidFrom, _snapshot.ValidFrom)
            || !Nullable.Equals(ValidTo, _snapshot.ValidTo);

    public static EditableFileDetailModel FromDto(EditableFileDetailDto detail)
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

    public EditableFileDetailDto ToDto()
    {
        return new EditableFileDetailDto
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

    public void UpdateSnapshot(EditableFileDetailDto detail)
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
            SetValidationErrors(pair.Key ?? string.Empty, pair.Value);
        }
    }

    public new IEnumerable<ValidationResult> GetErrors(string? propertyName = null)
    {
        var baseErrors = base.GetErrors(propertyName) ?? Enumerable.Empty<ValidationResult>();

        if (string.IsNullOrEmpty(propertyName))
        {
            if (_externalErrors.Count == 0)
            {
                return baseErrors;
            }

            return baseErrors.Concat(
                _externalErrors.SelectMany(static pair =>
                    pair.Value.Select(error => new ValidationResult(error, GetMemberNames(pair.Key)))));
        }

        if (_externalErrors.TryGetValue(propertyName, out var errors) && errors.Length > 0)
        {
            return baseErrors.Concat(errors.Select(error => new ValidationResult(error, GetMemberNames(propertyName))));
        }

        return baseErrors;
    }

    IEnumerable INotifyDataErrorInfo.GetErrors(string? propertyName) => GetErrors(propertyName);

    public void ResetValidation()
    {
        ClearErrors();
        ClearExternalErrors();
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
            SetValidationErrors(nameof(ValidFrom), Array.Empty<string>());
            SetValidationErrors(nameof(ValidTo), Array.Empty<string>());
            return;
        }

        if (ValidFrom is null || ValidTo is null)
        {
            SetValidationErrors(nameof(ValidFrom), ValidFrom is null ? new[] { "Datum začátku platnosti je povinné." } : Array.Empty<string>());
            SetValidationErrors(nameof(ValidTo), ValidTo is null ? new[] { "Datum ukončení platnosti je povinné." } : Array.Empty<string>());
            return;
        }

        if (ValidFrom > ValidTo)
        {
            SetValidationErrors(nameof(ValidFrom), new[] { "Začátek platnosti musí být dříve než konec." });
            SetValidationErrors(nameof(ValidTo), new[] { "Konec platnosti musí být po začátku." });
            return;
        }

        SetValidationErrors(nameof(ValidFrom), Array.Empty<string>());
        SetValidationErrors(nameof(ValidTo), Array.Empty<string>());
    }

    private static IEnumerable<string>? GetMemberNames(string propertyName)
    {
        return string.IsNullOrEmpty(propertyName) ? null : new[] { propertyName };
    }

    private void SetValidationErrors(string? propertyName, IEnumerable<string> errors)
    {
        propertyName ??= string.Empty;

        var normalizedErrors = errors?
            .Where(static error => !string.IsNullOrWhiteSpace(error))
            .Select(static error => error.Trim())
            .ToArray()
            ?? Array.Empty<string>();

        var hadManualErrors = _externalErrors.Count > 0;
        var propertyHadErrors = _externalErrors.TryGetValue(propertyName, out var existing);

        if (normalizedErrors.Length == 0)
        {
            if (!propertyHadErrors)
            {
                return;
            }

            _externalErrors.Remove(propertyName);
            RaiseManualErrorsChanged(propertyName, hadManualErrors);
            return;
        }

        if (propertyHadErrors && existing!.SequenceEqual(normalizedErrors, StringComparer.Ordinal))
        {
            return;
        }

        _externalErrors[propertyName] = normalizedErrors;
        RaiseManualErrorsChanged(propertyName, hadManualErrors);
    }

    private void RaiseManualErrorsChanged(string propertyName, bool hadManualErrors)
    {
        OnErrorsChanged(propertyName);

        var hasManualErrors = _externalErrors.Count > 0;

        if (hadManualErrors != hasManualErrors)
        {
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    private void ClearExternalErrors()
    {
        if (_externalErrors.Count == 0)
        {
            return;
        }

        var keys = _externalErrors.Keys.ToArray();
        _externalErrors.Clear();

        foreach (var key in keys)
        {
            OnErrorsChanged(key);
        }

        OnPropertyChanged(nameof(HasErrors));
    }
}
