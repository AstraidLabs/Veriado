using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Veriado.Infrastructure.FileSystem;

internal sealed class FileSystemMonitoringHostedService : BackgroundService
{
    private readonly IFileSystemMonitoringService _monitoringService;

    public FileSystemMonitoringHostedService(IFileSystemMonitoringService monitoringService)
    {
        _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _monitoringService.Start(stoppingToken);
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _monitoringService.Dispose();
        base.Dispose();
    }
}
