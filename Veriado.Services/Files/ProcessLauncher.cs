using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Veriado.Services.Files;

/// <summary>
/// Default implementation of <see cref="IProcessLauncher"/> using <see cref="Process.Start(ProcessStartInfo)"/>.
/// </summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    private readonly ILogger<ProcessLauncher> _logger;

    public ProcessLauncher(ILogger<ProcessLauncher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool TryStart(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        try
        {
            var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start process {FileName}.", startInfo.FileName);
            return false;
        }
    }
}
