using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Veriado.Contracts.Files;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.ViewModels.Files;

public sealed partial class FileListItemModel : ObservableObject
{
    private ValidityThresholds _validityThresholds;

    private static (DateTimeOffset? from, DateTimeOffset? to) NormalizeValidity(FileValidityDto? validity)
    {
        if (validity is null)
        {
            return (null, null);
        }

        var validFrom = validity.IssuedAt.ToLocalTime();
        var validTo = validity.ValidUntil.ToLocalTime();

        if (validTo < validFrom)
        {
            return (null, null);
        }

        return (validFrom, validTo);
    }

    public FileListItemModel(FileSummaryDto dto, DateTimeOffset referenceTime, ValidityThresholds thresholds)
    {
        Dto = dto ?? throw new ArgumentNullException(nameof(dto));
        var (from, to) = NormalizeValidity(dto.Validity);
        ValidFrom = from;
        ValidTo = to;
        _validityThresholds = thresholds;
        Validity = new ValidityInfo(ValidFrom, ValidTo, referenceTime, _validityThresholds);

        PhysicalState = dto.PhysicalState;
        PhysicalStatusMessage = dto.PhysicalStatusMessage;
    }

    public FileSummaryDto Dto { get; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }

    private ValidityInfo _validity;

    public ValidityInfo Validity
    {
        get => _validity;
        private set => SetProperty(ref _validity, value);
    }

    [ObservableProperty]
    private string? physicalState;

    [ObservableProperty]
    private string? physicalStatusMessage;

    public bool HasPhysicalIssue => !string.IsNullOrWhiteSpace(PhysicalStatusMessage);

    public bool HasPhysicalFile => !string.Equals(PhysicalState, "Missing", StringComparison.OrdinalIgnoreCase);

    public bool IsHealthy => string.Equals(PhysicalState, "Healthy", StringComparison.OrdinalIgnoreCase);

    public void RecomputeValidity(DateTimeOffset referenceTime, ValidityThresholds? thresholds = null)
    {
        if (thresholds.HasValue)
        {
            _validityThresholds = thresholds.Value;
        }

        Validity = new ValidityInfo(ValidFrom, ValidTo, referenceTime, _validityThresholds);
    }

    public void UpdatePhysicalStatus(string? newState, string? statusMessage)
    {
        PhysicalState = newState;
        PhysicalStatusMessage = statusMessage;
    }

    partial void OnPhysicalStateChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPhysicalFile));
        OnPropertyChanged(nameof(IsHealthy));
    }

    partial void OnPhysicalStatusMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPhysicalIssue));
    }
}
