using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Files;

public partial class FileValidityModel : ObservableObject
{
    [ObservableProperty]
    private DateTimeOffset issuedAt;

    [ObservableProperty]
    private DateTimeOffset validUntil;

    [ObservableProperty]
    private bool hasPhysicalCopy;

    [ObservableProperty]
    private bool hasElectronicCopy;

    public double TotalDays => (ValidUntil - IssuedAt).TotalDays;

    public bool IsExpired => ValidUntil < DateTimeOffset.UtcNow;

    public bool IsActive => IssuedAt <= DateTimeOffset.UtcNow && ValidUntil >= DateTimeOffset.UtcNow;
}
