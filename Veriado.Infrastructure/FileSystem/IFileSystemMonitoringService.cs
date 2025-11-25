using System;
using System.Threading;

namespace Veriado.Infrastructure.FileSystem;

public interface IFileSystemMonitoringService : IDisposable, IAsyncDisposable
{
    void Start(CancellationToken cancellationToken);
}
