namespace Veriado.Infrastructure.Persistence.Connections;

public interface ISqlitePathResolver
{
    string Resolve(SqliteResolutionScenario scenario);

    void EnsureStorageExists(string path);
}
