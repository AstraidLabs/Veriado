using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Veriado.Contracts.Files;

namespace Veriado.WinUI.ViewModels.Files;

public enum ValidityState
{
    None,
    Expired,
    ExpiringToday,
    ExpiringSoon,
    ExpiringLater,
    LongTerm,
}

public readonly record struct ValidityTooltipContext(DateTimeOffset? IssuedAt, DateTimeOffset? ValidUntil);

public partial class FileSummaryItemViewModel : ObservableObject
{
    public FileSummaryItemViewModel(FileSummaryDto dto, DateTimeOffset referenceTime)
    {
        Dto = dto ?? throw new ArgumentNullException(nameof(dto));
        UpdateValidity(referenceTime);
    }

    public FileSummaryDto Dto { get; }

    public bool HasValidity => ValidityState != ValidityState.None;

    public ValidityTooltipContext ValidityTooltipContext => new(ValidityIssuedAt, ValidityValidUntil);

    [ObservableProperty]
    private ValidityState validityState;

    [ObservableProperty]
    private int? validityDaysRemaining;

    [ObservableProperty]
    private DateTimeOffset? validityIssuedAt;

    [ObservableProperty]
    private DateTimeOffset? validityValidUntil;

    public void UpdateValidity(DateTimeOffset referenceTime)
    {
        if (Dto.Validity is { } validity)
        {
            var issuedAt = validity.IssuedAt.ToLocalTime();
            var validUntil = validity.ValidUntil.ToLocalTime();

            ValidityIssuedAt = issuedAt;
            ValidityValidUntil = validUntil;

            var referenceDate = referenceTime.ToLocalTime().Date;
            var validUntilDate = validUntil.Date;
            var daysRemaining = (validUntilDate - referenceDate).Days;

            ValidityDaysRemaining = daysRemaining;
            ValidityState = DetermineState(daysRemaining);
        }
        else
        {
            ValidityIssuedAt = null;
            ValidityValidUntil = null;
            ValidityDaysRemaining = null;
            ValidityState = ValidityState.None;
        }
    }

    private static ValidityState DetermineState(int daysRemaining)
    {
        if (daysRemaining < 0)
        {
            return ValidityState.Expired;
        }

        if (daysRemaining == 0)
        {
            return ValidityState.ExpiringToday;
        }

        if (daysRemaining <= 7)
        {
            return ValidityState.ExpiringSoon;
        }

        if (daysRemaining <= 30)
        {
            return ValidityState.ExpiringLater;
        }

        return ValidityState.LongTerm;
    }

    partial void OnValidityStateChanged(ValidityState value)
    {
        OnPropertyChanged(nameof(HasValidity));
    }

    partial void OnValidityIssuedAtChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(ValidityTooltipContext));
    }

    partial void OnValidityValidUntilChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(ValidityTooltipContext));
    }
}
