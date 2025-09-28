using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Veriado.Infrastructure.Persistence.Migrations;

namespace Veriado.Infrastructure.Persistence.DesignTime;

/// <summary>
/// Configures EF Core design-time services for the infrastructure assembly.
/// </summary>
public sealed class InfrastructureDesignTimeServices : IDesignTimeServices
{
    /// <inheritdoc />
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        services.AddSingleton<IMigrationsIdGenerator, LenientMigrationsIdGenerator>();
        services.AddSingleton<IMigrationsAssembly, LenientMigrationsAssembly>();
    }
}
