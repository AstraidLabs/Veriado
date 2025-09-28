namespace Veriado.Infrastructure.DependencyInjection;

internal sealed class InfrastructureInitializationState
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public async Task<bool> EnsureInitializedAsync(Func<Task> initializer, CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return false;
            }

            await initializer().ConfigureAwait(false);
            _initialized = true;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
