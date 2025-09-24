using System;
using System.IO;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class InfrastructureConfigProvider : IInfrastructureConfigProvider
{
    public string GetDatabasePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(root, "Veriado");
        return Path.Combine(directory, "veriado.db");
    }

    public void EnsureStorageExists(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
