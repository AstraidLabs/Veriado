using System.Threading;
using System.Threading.Tasks;

namespace Veriado.Services.Abstractions;

public enum AppTheme
{
    Default,
    Light,
    Dark,
}

public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SetThemeAsync(AppTheme theme, CancellationToken cancellationToken = default);
}
