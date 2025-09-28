namespace Veriado.WinUI.Services.Abstractions;

public interface IExceptionHandler
{
    string Handle(Exception exception);
}
