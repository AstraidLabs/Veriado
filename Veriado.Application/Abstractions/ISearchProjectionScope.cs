using System;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Appl.Abstractions;

public interface ISearchProjectionScope
{
    void EnsureActive();

    Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
}
