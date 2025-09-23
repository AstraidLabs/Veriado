using System;

namespace Veriado.Services.Abstractions;

public interface IInfrastructureConfigProvider
{
    string GetDatabasePath();

    void EnsureStorageExists(string path);
}
