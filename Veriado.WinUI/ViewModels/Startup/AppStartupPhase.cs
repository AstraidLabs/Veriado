namespace Veriado.WinUI.ViewModels.Startup;

public enum AppStartupPhase
{
    Bootstrap,
    StorageCheck,
    HostBuild,
    Migrations,
    HotState,
    Shell
}

public interface IStartupReporter
{
    void Report(AppStartupPhase phase, string message);
}
