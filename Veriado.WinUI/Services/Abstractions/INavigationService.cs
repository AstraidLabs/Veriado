namespace Veriado.WinUI.Services.Abstractions;

public interface INavigationService
{
    void AttachHost(INavigationHost host);

    void NavigateTo(object view, object? viewModel = null);

    void NavigateDetail(object view, object? viewModel = null);
}

public interface INavigationHost
{
    object? CurrentContent { get; set; }

    object? CurrentDetail { get; set; }
}
