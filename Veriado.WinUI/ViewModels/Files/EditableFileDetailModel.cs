using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Veriado.Appl.Files.Contracts;
using Veriado.WinUI.ViewModels.Validation;
using DataAnnotationsValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;
using ValidationResult = Veriado.WinUI.ViewModels.Validation.ValidationResult;

namespace Veriado.WinUI.ViewModels.Files;

/// <summary>
/// Represents an editable snapshot of a file detail with validation support for the dialog.
/// </summary>
[Flags]
public enum FileValidationScope
{
    None = 0,
    Metadata = 1,
    Validity = 2,
    All = Metadata | Validity,
}

public sealed partial class EditableFileDetailModel : ObservableValidator, INotifyDataErrorInfo
{
    private EditableFileDetailDto _snapshot = null!;
    private readonly Dictionary<string, string[]> _externalErrors = new(StringComparer.Ordinal);
    private event EventHandler<DataErrorsChangedEventArgs>? _manualErrorsChanged;

    public new event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged
    {
        add
        {
            base.ErrorsChanged += value;
            _manualErrorsChanged += value;
        }
        remove
        {
            base.ErrorsChanged -= value;
            _manualErrorsChanged -= value;
        }
    }

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
    [RegularExpression(@"^\s*[^/\s]+/[^/\s]+\s*$", ErrorMessage = "MIME typ musí být ve formátu type/subtype.")]
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
            author = NormalizeAuthor(detail.Author),
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
            Author = NormalizeAuthor(Author),
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

        Id = detail.Id;
        Extension = detail.Extension;
        FileName = detail.FileName;
        MimeType = detail.MimeType;
        Author = NormalizeAuthor(detail.Author);
        IsReadOnly = detail.IsReadOnly;
        Size = detail.Size;
        CreatedAt = detail.CreatedAt;
        ModifiedAt = detail.ModifiedAt;
        Version = detail.Version;
        ValidFrom = detail.ValidFrom;
        ValidTo = detail.ValidTo;

        ResetValidation();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
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

    public new IEnumerable<DataAnnotationsValidationResult> GetErrors(string? propertyName = null)
    {
        var baseErrors = base.GetErrors(propertyName) ?? Enumerable.Empty<DataAnnotationsValidationResult>();

        if (string.IsNullOrEmpty(propertyName))
        {
            if (_externalErrors.Count == 0)
            {
                return baseErrors;
            }

            return baseErrors.Concat(
                _externalErrors.SelectMany(static pair =>
                    pair.Value.Select(error => new DataAnnotationsValidationResult(error, GetMemberNames(pair.Key)))));
        }

        if (_externalErrors.TryGetValue(propertyName, out var errors) && errors.Length > 0)
        {
            return baseErrors.Concat(errors.Select(error => new DataAnnotationsValidationResult(error, GetMemberNames(propertyName))));
        }

        return baseErrors;
    }

    IEnumerable INotifyDataErrorInfo.GetErrors(string? propertyName) => GetErrors(propertyName);

    public void ResetValidation()
    {
        ClearErrors();
        ClearExternalErrors();
    }

    public void ValidateAll() => Validate(FileValidationScope.All);

    public ValidationResult Validate(FileValidationScope scope)
    {
        var result = new ValidationResult();

        if (scope.HasFlag(FileValidationScope.Metadata))
        {
            ValidateProperty(FileName, nameof(FileName));
            ValidateProperty(MimeType, nameof(MimeType));
            ValidateAuthor();

            AddPropertyErrors(result, nameof(FileName));
            AddPropertyErrors(result, nameof(MimeType));
            AddPropertyErrors(result, nameof(Author));
        }

        if (scope.HasFlag(FileValidationScope.Validity))
        {
            ValidateValidityRange(result);
        }

        return result;
    }

    public ValidationResult Validate() => Validate(FileValidationScope.All);

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
        var normalized = NormalizeAuthor(value);

        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            author = normalized;
            OnPropertyChanged(nameof(Author));
        }

        ValidateAuthor();
    }

    partial void OnValidFromChanged(DateTimeOffset? value)
    {
        ValidateValidityRange();
    }

    partial void OnValidToChanged(DateTimeOffset? value)
    {
        ValidateValidityRange();
    }

    private void ValidateAuthor()
    {
        ValidateProperty(Author, nameof(Author));
        SetValidationErrors(nameof(Author), Array.Empty<string>());
    }

    private void ValidateValidityRange(ValidationResult? result = null)
    {
        if (ValidFrom is null && ValidTo is null)
        {
            SetValidationErrors(nameof(ValidFrom), Array.Empty<string>());
            SetValidationErrors(nameof(ValidTo), Array.Empty<string>());
            return;
        }

        if (ValidFrom is null || ValidTo is null)
        {
            const string message = "Both validity dates must be set.";
            SetValidationErrors(nameof(ValidFrom), new[] { message });
            SetValidationErrors(nameof(ValidTo), new[] { message });
            result?.AddError(nameof(ValidFrom), message);
            result?.AddError(nameof(ValidTo), message);
            return;
        }

        if (ValidFrom > ValidTo)
        {
            const string message = "Valid from cannot be after valid to.";
            SetValidationErrors(nameof(ValidFrom), new[] { message });
            SetValidationErrors(nameof(ValidTo), new[] { message });
            result?.AddError(nameof(ValidFrom), message);
            result?.AddError(nameof(ValidTo), message);
            return;
        }

        SetValidationErrors(nameof(ValidFrom), Array.Empty<string>());
        SetValidationErrors(nameof(ValidTo), Array.Empty<string>());
        if (result is not null)
        {
            AddPropertyErrors(result, nameof(ValidFrom));
            AddPropertyErrors(result, nameof(ValidTo));
        }
    }

    private void ValidateValidityRange() => ValidateValidityRange(null);

    private void AddPropertyErrors(ValidationResult result, string propertyName)
    {
        foreach (var error in GetErrors(propertyName))
        {
            if (!string.IsNullOrWhiteSpace(error?.ErrorMessage))
            {
                result.AddError(propertyName, error!.ErrorMessage!);
            }
        }
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
        _manualErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));

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
            _manualErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(key));
        }

        OnPropertyChanged(nameof(HasErrors));
    }
    internal static string? NormalizeAuthor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
