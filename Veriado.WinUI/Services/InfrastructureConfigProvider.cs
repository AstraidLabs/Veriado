using System.IO;
using Veriado.WinUI.Errors;

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
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(path))
        {
            using var _ = File.Create(path);
        }
    }

    public async Task EnsureStorageExistsSafe()
    {
        var path = GetDatabasePath();

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(path))
            {
                await using var _ = File.Create(path);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InitializationException(
                $"Nelze vytvořit databázi v '{path}'. Nedostatečná oprávnění.",
                ex,
                "Spusťte aplikaci s vyššími oprávněními nebo zvolte jiný adresář.");
        }
        catch (IOException ex)
        {
            throw new InitializationException(
                $"Chyba IO při vytváření databáze v '{path}'.",
                ex,
                "Zkontrolujte volné místo a uzamčení souboru jiným procesem.");
        }
    }
}
