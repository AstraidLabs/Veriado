using Microsoft.Extensions.Hosting;

namespace Veriado.Infrastructure.Search.Outbox;

/// <summary>
/// Background worker that periodically drains the deferred indexing outbox.
/// </summary>
internal sealed class OutboxWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);

    private readonly OutboxDrainService _drainService;
    private readonly InfrastructureOptions _options;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(
        OutboxDrainService drainService,
        InfrastructureOptions options,
        ILogger<OutboxWorker> logger)
    {
        _drainService = drainService;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.SearchIndexingMode != SearchIndexingMode.Outbox)
        {
            _logger.LogDebug("Outbox worker skipped because indexing mode is {Mode}", _options.SearchIndexingMode);
            return;
        }

        _logger.LogInformation("Outbox worker started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var processed = await _drainService.DrainAsync(stoppingToken).ConfigureAwait(false);
                if (processed == 0)
                {
                    await Task.Delay(IdleDelay, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Outbox worker stopping");
        }
    }
}
