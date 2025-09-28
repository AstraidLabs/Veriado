namespace Veriado.Infrastructure.Persistence.Configurations;

/// <summary>
/// Provides contextual access to <see cref="InfrastructureOptions"/> for entity type configuration classes.
/// </summary>
internal static class InfrastructureModel
{
    private sealed class Scope : IDisposable
    {
        private readonly InfrastructureOptions? _previous;

        public Scope(InfrastructureOptions? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            _current.Value = _previous;
        }
    }

    private static readonly AsyncLocal<InfrastructureOptions?> _current = new();

    /// <summary>
    /// Gets the active options for model configuration.
    /// </summary>
    public static InfrastructureOptions Current => _current.Value ?? throw new InvalidOperationException("Infrastructure options have not been initialised for the current model build.");

    /// <summary>
    /// Applies the provided options for the duration of the returned scope.
    /// </summary>
    /// <param name="options">The infrastructure options instance.</param>
    /// <returns>An <see cref="IDisposable"/> scope that restores the previous options on dispose.</returns>
    public static IDisposable UseOptions(InfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var previous = _current.Value;
        _current.Value = options;
        return new Scope(previous);
    }
}
