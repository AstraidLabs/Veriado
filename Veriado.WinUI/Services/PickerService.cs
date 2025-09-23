using System.Threading;
using System.Threading.Tasks;
using Veriado.Presentation.Services;

namespace Veriado.Services;

/// <summary>
/// Temporary picker service implementation that will be replaced with platform-specific pickers.
/// </summary>
internal sealed class PickerService : IPickerService
{
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    public Task<PickedFile?> PickFileAsync(CancellationToken cancellationToken)
        => Task.FromResult<PickedFile?>(null);
}
