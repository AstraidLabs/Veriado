namespace Veriado.WinUI.Services.Abstractions;

public interface INavigationService
{
    object? CurrentContent { get; }
    object? CurrentDetail { get; }

    void NavigateToContent(object view);
    void NavigateToDetail(object? view);
    void ClearDetail();
}
