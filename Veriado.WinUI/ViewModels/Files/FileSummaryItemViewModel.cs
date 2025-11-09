using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Veriado.Contracts.Files;

namespace Veriado.WinUI.ViewModels.Files;

public enum ValidityStatus
{
    None,
    Ok,
    Upcoming,
    Soon,
    Expired,
}

public partial class FileSummaryItemViewModel : ObservableObject
{
    public FileSummaryItemViewModel(FileSummaryDto dto, DateTimeOffset referenceTime)
    {
        Dto = dto ?? throw new ArgumentNullException(nameof(dto));
        RecomputeValidity(referenceTime);
    }

    public FileSummaryDto Dto { get; }

    public bool HasValidity => ValidFrom.HasValue && ValidTo.HasValue;

    private DateTimeOffset? _validFrom;
    public DateTimeOffset? ValidFrom
    {
        get => _validFrom;
        private set
        {
            if (SetProperty(ref _validFrom, value))
            {
                OnPropertyChanged(nameof(HasValidity));
            }
        }
    }

    private DateTimeOffset? _validTo;
    public DateTimeOffset? ValidTo
    {
        get => _validTo;
        private set
        {
            if (SetProperty(ref _validTo, value))
            {
                OnPropertyChanged(nameof(HasValidity));
            }
        }
    }

    private int? _daysRemaining;
    public int? DaysRemaining
    {
        get => _daysRemaining;
        private set => SetProperty(ref _daysRemaining, value);
    }

    private ValidityStatus _validityStatus;
    public ValidityStatus ValidityStatus
    {
        get => _validityStatus;
        private set => SetProperty(ref _validityStatus, value);
    }

    public void RecomputeValidity(DateTimeOffset now)
    {
        if (Dto.Validity is not { } validity || validity.ValidUntil < validity.IssuedAt)
        {
            ValidFrom = null;
            ValidTo = null;
            DaysRemaining = null;
            ValidityStatus = ValidityStatus.None;
            return;
        }

        var validFrom = validity.IssuedAt.ToLocalTime();
        var validTo = validity.ValidUntil.ToLocalTime();

        ValidFrom = validFrom;
        ValidTo = validTo;

        var referenceDate = now.ToLocalTime().Date;
        var days = (validTo.Date - referenceDate).Days;
        DaysRemaining = days;

        ValidityStatus = days <= 0 ? ValidityStatus.Expired
                       : days <= 7 ? ValidityStatus.Soon
                       : days <= 30 ? ValidityStatus.Upcoming
                       : ValidityStatus.Ok;
    }
}
