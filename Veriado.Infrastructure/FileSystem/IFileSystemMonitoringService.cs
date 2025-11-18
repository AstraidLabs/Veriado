using System;
using System.Threading;

namespace Veriado.Infrastructure.FileSystem;

public interface IFileSystemMonitoringService : IDisposable
{
    void Start(CancellationToken cancellationToken);
}
