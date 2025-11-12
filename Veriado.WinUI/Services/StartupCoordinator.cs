using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.Infrastructure.Lifecycle;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.Views.Shell;

namespace Veriado.WinUI.Services;

public sealed class StartupCoordinator : IStartupCoordinator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAppLifecycleService _lifecycleService;
    private readonly IWindowProvider _windowProvider;
    private readonly IDispatcherService _dispatcherService;
    private readonly IThemeService _themeService;
    private readonly IHotStateService _hotStateService;
    private readonly ILogger<StartupCoordinator> _logger;

    public StartupCoordinator(
        IServiceProvider serviceProvider,
        IAppLifecycleService lifecycleService,
        IWindowProvider windowProvider,
        IDispatcherService dispatcherService,
        IThemeService themeService,
        IHotStateService hotStateService,
        ILogger<StartupCoordinator> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _lifecycleService = lifecycleService ?? throw new ArgumentNullException(nameof(lifecycleService));
        _windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
        _dispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _hotStateService = hotStateService ?? throw new ArgumentNullException(nameof(hotStateService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StartupResult> RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Coordinating application startup.");

        try
        {
            await _lifecycleService.StartAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Startup was canceled before lifecycle completed.");
            throw;
        }

        await _hotStateService.InitializeAsync().ConfigureAwait(true);

        var shell = _serviceProvider.GetRequiredService<MainShell>();

        _windowProvider.SetWindow(shell);
        _dispatcherService.ResetDispatcher(shell.DispatcherQueue);
        await _themeService.InitializeAsync().ConfigureAwait(true);

        _logger.LogInformation("Startup coordinated successfully.");
        return new StartupResult(shell);
    }
}
