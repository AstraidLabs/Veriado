using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Veriado.Infrastructure.DependencyInjection;

internal sealed class CompositeHostedService : IHostedService, IDisposable, IAsyncDisposable
{
    private readonly IReadOnlyList<IHostedService> _hostedServices;

    public CompositeHostedService(IReadOnlyList<IHostedService> hostedServices)
    {
        ArgumentNullException.ThrowIfNull(hostedServices);
        _hostedServices = hostedServices;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var service in _hostedServices)
        {
            await service.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        for (var index = _hostedServices.Count - 1; index >= 0; index--)
        {
            await _hostedServices[index].StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        foreach (var service in _hostedServices)
        {
            if (service is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var service in _hostedServices)
        {
            if (service is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (service is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }
}
