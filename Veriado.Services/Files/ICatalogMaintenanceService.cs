namespace Veriado.Services.Files;

public interface ICatalogMaintenanceService
{
    Task ClearCatalogAsync(CancellationToken cancellationToken = default);
}
