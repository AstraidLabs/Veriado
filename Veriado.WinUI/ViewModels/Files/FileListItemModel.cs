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

    public void RecomputeValidity(DateTimeOffset referenceTime, ValidityThresholds? thresholds = null)
    {
        if (thresholds.HasValue)
        {
            _validityThresholds = thresholds.Value;
        }

        Validity = new ValidityInfo(ValidFrom, ValidTo, referenceTime, _validityThresholds);
    }
}
