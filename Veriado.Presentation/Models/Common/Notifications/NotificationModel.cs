using CommunityToolkit.Mvvm.ComponentModel;

namespace Veriado.Presentation.Models.Common.Notifications;

public enum NotificationSeverity
{
    Information,
    Success,
    Warning,
    Error,
}

public partial class NotificationModel : ObservableObject
{
    [ObservableProperty]
    private NotificationSeverity severity = NotificationSeverity.Information;

    [ObservableProperty]
    private string message = string.Empty;

    [ObservableProperty]
    private bool isOpen;
}
