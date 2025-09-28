using Microsoft.Extensions.Logging;

namespace Veriado.WinUI.Services;

public sealed class ExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ExceptionHandler> _logger;

    public ExceptionHandler(ILogger<ExceptionHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Handle(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
        {
            return "Operace byla zrušena.";
        }

        _logger.LogError(exception, "Unhandled exception in view model execution.");
        return "Došlo k neočekávané chybě.";
    }
}
