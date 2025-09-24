using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Veriado.WinUI.Infrastructure;

public static class CommandForwarder
{
    public static void TryExecute(ICommand? command, object? parameter, ILogger? logger = null)
    {
        try
        {
            if (command?.CanExecute(parameter) == true)
            {
                command.Execute(parameter);
            }
        }
        catch (Exception ex)
        {
            LogErrorSafely(logger, ex, "Command execution failed (param: {Param})", parameter);
        }
    }

    public static async Task TryExecuteAsync(object? command, object? parameter, ILogger? logger = null)
    {
        try
        {
            switch (command)
            {
                case null:
                    return;
                case IAsyncRelayCommand asyncCommand when asyncCommand.CanExecute(parameter):
                    await asyncCommand.ExecuteAsync(parameter).ConfigureAwait(false);
                    break;
                case ICommand sync when sync.CanExecute(parameter):
                    sync.Execute(parameter);
                    break;
            }
        }
        catch (Exception ex)
        {
            LogErrorSafely(logger, ex, "Async command execution failed (param: {Param})", parameter);
        }
    }

    private static void LogErrorSafely(ILogger? logger, Exception exception, string message, object? parameter)
    {
        if (logger is null)
        {
            return;
        }

        try
        {
            logger.LogError(exception, message, parameter);
        }
        catch (ObjectDisposedException)
        {
            // The logging infrastructure may already be disposed (e.g. during application shutdown).
            // Swallow the exception because the original command failure has already been handled.
        }
    }
}
