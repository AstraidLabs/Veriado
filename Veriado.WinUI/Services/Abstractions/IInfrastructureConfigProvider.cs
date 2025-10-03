namespace Veriado.WinUI.Services.Abstractions;

public interface IInfrastructureConfigProvider
{
    string GetDatabasePath();

    void EnsureStorageExists(string path);

    Task EnsureStorageExistsSafe();
}
