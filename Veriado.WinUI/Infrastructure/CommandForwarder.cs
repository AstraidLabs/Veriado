using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Veriado.Infrastructure;

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
            logger?.LogError(ex, "Command execution failed (param: {Param})", parameter);
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
            logger?.LogError(ex, "Async command execution failed (param: {Param})", parameter);
        }
    }
}
