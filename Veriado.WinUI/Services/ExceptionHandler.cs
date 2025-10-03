using Microsoft.Extensions.Logging;
using Veriado.Appl.Common;

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

        if (exception is ValidationException validationException)
        {
            var message = BuildValidationMessage(validationException);
            _logger.LogWarning(exception, "Validation failure in view model execution.");
            return message;
        }

        if (exception is OperationCanceledException)
        {
            return "Operace byla zrušena.";
        }

        _logger.LogError(exception, "Unhandled exception in view model execution.");
        return "Došlo k neočekávané chybě.";
    }

    private static string BuildValidationMessage(ValidationException exception)
    {
        if (exception.Errors.Count == 0)
        {
            return "Zadané parametry nejsou platné.";
        }

        return string.Join(Environment.NewLine, exception.Errors);
    }
}
