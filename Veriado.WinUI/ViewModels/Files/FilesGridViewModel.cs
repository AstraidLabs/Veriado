using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Contracts.Common;
using Veriado.Contracts.Files;
using Veriado.Mappers;
using Veriado.Models.Files;
using Veriado.Services;
using Veriado.Services.Files;
using Veriado.ViewModels.Base;

namespace Veriado.ViewModels.Files;

public sealed partial class FilesGridViewModel : ViewModelBase
{
    private readonly IFileQueryService _fileQueryService;
    private readonly INavigationService _navigationService;

    public FilesGridViewModel(IFileQueryService fileQueryService, INavigationService navigationService)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

        UpdateCreatedRange();
    }

    public ObservableCollection<FileListItemModel> Items { get; } = new();

    [ObservableProperty]
    private string? queryText;

    [ObservableProperty]
    private int pageIndex;

    [ObservableProperty]
    private int pageSize = 50;

    [ObservableProperty]
    private FileSortSpecDto? sort;

    [ObservableProperty]
    private DateTimeOffset? createdFrom;

    [ObservableProperty]
    private DateTimeOffset? createdTo;

    [ObservableProperty]
    private int createdDaysFrom;

    [ObservableProperty]
    private int createdDaysTo = 365;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await SafeExecuteAsync(async ct =>
        {
            var result = await _fileQueryService.GetGridAsync(BuildQuery(), ct).ConfigureAwait(false);

            Items.Clear();
            foreach (var dto in result.Items)
            {
                Items.Add(dto.ToFileListItemModel());
            }

            StatusMessage = result.TotalCount == 0
                ? "Nebyla nalezena žádná data."
                : $"Načteno {Items.Count} z {result.TotalCount} položek.";
        }, "Načítám položky…");
    }

    [RelayCommand]
    private void OpenDetail(Guid id)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        _navigationService.NavigateToFileDetail(id);
    }

    private FileGridQueryDto BuildQuery()
    {
        var effectivePage = PageIndex < 0 ? 0 : PageIndex;
        var effectiveSize = PageSize <= 0 ? 50 : PageSize;

        return new FileGridQueryDto
        {
            Text = string.IsNullOrWhiteSpace(QueryText) ? null : QueryText,
            CreatedFromUtc = CreatedFrom,
            CreatedToUtc = CreatedTo,
            Sort = Sort is not null ? new List<FileSortSpecDto> { Sort } : new List<FileSortSpecDto>(),
            Page = new PageRequest
            {
                Page = effectivePage + 1,
                PageSize = effectiveSize,
            },
        };
    }

    partial void OnCreatedDaysFromChanged(int value) => UpdateCreatedRange();

    partial void OnCreatedDaysToChanged(int value) => UpdateCreatedRange();

    private void UpdateCreatedRange()
    {
        var lower = CreatedDaysFrom < 0 ? 0 : CreatedDaysFrom;
        var upper = CreatedDaysTo < 0 ? 0 : CreatedDaysTo;

        if (upper < lower)
        {
            upper = lower;
            if (CreatedDaysTo != upper)
            {
                CreatedDaysTo = upper;
                return;
            }
        }

        if (lower != CreatedDaysFrom)
        {
            CreatedDaysFrom = lower;
            return;
        }

        if (upper != CreatedDaysTo)
        {
            CreatedDaysTo = upper;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        CreatedFrom = now.AddDays(-upper);
        CreatedTo = now.AddDays(-lower);
    }
}
