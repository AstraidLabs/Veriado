using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Veriado.WinUI.ViewModels.Startup;

internal sealed class StartupDiagnosticsLog
{
    private readonly List<StartupDiagnosticsEntry> _entries = new();
    private readonly string _logDirectory;

    public StartupDiagnosticsLog()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logDirectory = Path.Combine(root, "Veriado", "logs");
    }

    public void Clear() => _entries.Clear();

    public void RecordStart(AppStartupPhase phase, string message)
    {
        _entries.Add(StartupDiagnosticsEntry.Create(phase, StartupStepStatus.Running, message));
    }

    public void RecordUpdate(AppStartupPhase phase, string message)
    {
        _entries.Add(StartupDiagnosticsEntry.Create(phase, StartupStepStatus.Running, message));
    }

    public void RecordCompletion(
        AppStartupPhase phase,
        StartupStepStatus status,
        TimeSpan duration,
        string? message,
        string? detail)
    {
        _entries.Add(StartupDiagnosticsEntry.Create(phase, status, message, duration, detail));
    }

    public void RecordFailure(AppStartupPhase phase, Exception exception, TimeSpan duration)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        _entries.Add(StartupDiagnosticsEntry.Create(phase, StartupStepStatus.Failed, exception.Message, duration, exception.ToString()));
    }

    public void RecordWarning(AppStartupPhase phase, string message, Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        _entries.Add(StartupDiagnosticsEntry.Create(phase, StartupStepStatus.Warning, message, null, exception.ToString()));
    }

    public async Task<string?> FlushAsync(Exception? exception, bool safeMode, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        if (_entries.Count == 0 && exception is null)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_logDirectory);
            var fileName = $"startup-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log";
            var path = Path.Combine(_logDirectory, fileName);

            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);

            await writer.WriteLineAsync($"Veriado startup diagnostics ({DateTimeOffset.Now:u})").ConfigureAwait(false);
            await writer.WriteLineAsync($"Safe mode: {safeMode}").ConfigureAwait(false);
            await writer.WriteLineAsync($"Version: {GetAppVersion() ?? "unknown"}").ConfigureAwait(false);
            await writer.WriteLineAsync($"OS: {Environment.OSVersion}").ConfigureAwait(false);
            await writer.WriteLineAsync($"Process: {Environment.ProcessPath ?? "unknown"}").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);

            foreach (var entry in _entries)
            {
                await writer.WriteLineAsync(entry.ToString()).ConfigureAwait(false);
            }

            if (exception is not null)
            {
                await writer.WriteLineAsync().ConfigureAwait(false);
                await writer.WriteLineAsync("Final exception:").ConfigureAwait(false);
                await writer.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
            }

            await writer.FlushAsync().ConfigureAwait(false);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetAppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version?.ToString();
    }

    private sealed record StartupDiagnosticsEntry(
        DateTimeOffset Timestamp,
        AppStartupPhase Phase,
        StartupStepStatus Status,
        string? Message,
        TimeSpan? Duration,
        string? Detail)
    {
        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0:u} [{1}] {2}",
                Timestamp,
                Status,
                Phase);

            if (!string.IsNullOrWhiteSpace(Message))
            {
                builder.Append(':').Append(' ').Append(Message);
            }

            if (Duration is TimeSpan elapsed)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, " (trvalo {0:F0} ms)", elapsed.TotalMilliseconds);
            }

            if (!string.IsNullOrWhiteSpace(Detail))
            {
                builder.AppendLine();
                builder.Append("    ");
                builder.Append(Detail!.Replace(Environment.NewLine, Environment.NewLine + "    ", StringComparison.Ordinal));
            }

            return builder.ToString();
        }

        public static StartupDiagnosticsEntry Create(
            AppStartupPhase phase,
            StartupStepStatus status,
            string? message,
            TimeSpan? duration = null,
            string? detail = null) => new(DateTimeOffset.Now, phase, status, message, duration, detail);
    }
}
