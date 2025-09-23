using System;

namespace Veriado.Services.Abstractions;

public interface IExceptionHandler
{
    string Handle(Exception exception);
}
