using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.Services;

public sealed class NavigationService : ObservableObject, INavigationService
{
    private object? _currentContent;
    private object? _currentDetail;

    public object? CurrentContent
    {
        get => _currentContent;
        private set => SetProperty(ref _currentContent, value);
    }

    public object? CurrentDetail
    {
        get => _currentDetail;
        private set => SetProperty(ref _currentDetail, value);
    }

    public void NavigateToContent(object view)
    {
        ArgumentNullException.ThrowIfNull(view);
        CurrentContent = view;
        ClearDetail();
    }

    public void NavigateToDetail(object? view)
    {
        CurrentDetail = view;
    }

    public void ClearDetail()
    {
        CurrentDetail = null;
    }
}
