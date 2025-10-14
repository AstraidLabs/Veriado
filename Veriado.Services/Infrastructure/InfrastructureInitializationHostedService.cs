using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.DependencyInjection;

namespace Veriado.Services.Infrastructure;

/// <summary>
/// Ensures the SQLite infrastructure is initialised when the host starts.
/// </summary>
internal sealed class InfrastructureInitializationHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InfrastructureInitializationHostedService> _logger;

    public InfrastructureInitializationHostedService(
        IServiceProvider serviceProvider,
        ILogger<InfrastructureInitializationHostedService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _serviceProvider
                .InitializeInfrastructureAsync(cancellationToken, nameof(InfrastructureInitializationHostedService))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize infrastructure during startup.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
