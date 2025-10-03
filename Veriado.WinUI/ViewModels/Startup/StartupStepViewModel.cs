namespace Veriado.WinUI.ViewModels.Startup;

public enum StartupStepStatus
{
    Pending,
    Running,
    Succeeded,
    Warning,
    Failed
}

public partial class StartupStepViewModel : ObservableObject
{
    public StartupStepViewModel(AppStartupPhase phase, string title)
    {
        Phase = phase;
        Title = title ?? throw new ArgumentNullException(nameof(title));
    }

    public AppStartupPhase Phase { get; }

    public string Title { get; }

    [ObservableProperty]
    private StartupStepStatus status = StartupStepStatus.Pending;

    [ObservableProperty]
    private string? message;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private TimeSpan? duration;

    [ObservableProperty]
    private DateTimeOffset? startedAt;

    public string Glyph => Status switch
    {
        StartupStepStatus.Pending => "\uE823", // Clock
        StartupStepStatus.Running => "\uE9F5", // Sync
        StartupStepStatus.Succeeded => "\uE73E", // Checkmark
        StartupStepStatus.Warning => "\uE7BA", // Warning
        StartupStepStatus.Failed => "\uEA39", // Error badge
        _ => string.Empty
    };

    public string StatusText => Status switch
    {
        StartupStepStatus.Pending => "Čeká",
        StartupStepStatus.Running => "Probíhá",
        StartupStepStatus.Succeeded => "Hotovo",
        StartupStepStatus.Warning => "Varování",
        StartupStepStatus.Failed => "Chyba",
        _ => Status.ToString()
    };

    public string DurationText => Duration is TimeSpan elapsed
        ? (elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:F1} s"
            : $"{elapsed.TotalMilliseconds:F0} ms")
        : string.Empty;

    public bool HasDuration => Duration.HasValue;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public void Reset()
    {
        Status = StartupStepStatus.Pending;
        Message = null;
        ErrorMessage = null;
        Duration = null;
        StartedAt = null;
    }

    public void Start(string message)
    {
        StartedAt = DateTimeOffset.UtcNow;
        Status = StartupStepStatus.Running;
        Message = message;
        ErrorMessage = null;
        Duration = null;
    }

    public void Complete(TimeSpan elapsed, StartupStepStatus status)
    {
        Duration = elapsed;
        Status = status;
    }

    public void Fail(Exception exception, TimeSpan elapsed)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        Duration = elapsed;
        ErrorMessage = exception.Message;
        Status = StartupStepStatus.Failed;
    }

    public void Warn(string message, Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        Message = message;
        ErrorMessage = exception.Message;
        Status = StartupStepStatus.Warning;
    }

    partial void OnDurationChanged(TimeSpan? value)
    {
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(HasDuration));
    }

    partial void OnStatusChanged(StartupStepStatus value)
    {
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }
}
