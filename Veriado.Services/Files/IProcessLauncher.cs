using System.Diagnostics;

namespace Veriado.Services.Files;

/// <summary>
/// Provides a testable abstraction for starting external processes.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>
    /// Attempts to start a process using the provided <see cref="ProcessStartInfo"/>.
    /// </summary>
    /// <param name="startInfo">The process start information.</param>
    /// <returns><c>true</c> if the process was started; otherwise <c>false</c>.</returns>
    bool TryStart(ProcessStartInfo startInfo);
}
