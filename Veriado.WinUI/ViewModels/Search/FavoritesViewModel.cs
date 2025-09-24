using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Search;
using Veriado.Services.Files;
using Veriado.WinUI.ViewModels.Base;
using Veriado.WinUI.Services.Abstractions;

namespace Veriado.WinUI.ViewModels.Search;

public sealed partial class FavoritesViewModel : ViewModelBase
{
    private readonly IFileQueryService _fileQueryService;

    public FavoritesViewModel(
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler,
        IFileQueryService fileQueryService)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
    }

    public ObservableCollection<SearchFavoriteItem> Items { get; } = new();

    [RelayCommand]
    private async Task LoadAsync()
    {
        await SafeExecuteAsync(async ct =>
        {
            var favorites = await _fileQueryService.GetFavoritesAsync(ct);

            Items.Clear();
            foreach (var favorite in favorites)
            {
                Items.Add(favorite);
            }

            if (Items.Count == 0)
            {
                StatusService.Info("Žádné uložené oblíbené položky.");
            }
            else
            {
                StatusService.Info($"Načteno {Items.Count} oblíbených vyhledávání.");
            }
        }, "Načítám oblíbená vyhledávání…");
    }

    [RelayCommand]
    private async Task RemoveAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            await _fileQueryService.RemoveFavoriteAsync(id, ct);
            StatusService.Info("Oblíbené vyhledávání bylo odstraněno.");
        }, "Odstraňuji oblíbené vyhledávání…");

        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddAsync(SearchFavoriteDefinition? favorite)
    {
        if (favorite is null || string.IsNullOrWhiteSpace(favorite.Name))
        {
            HasError = true;
            StatusService.Error("Název oblíbeného vyhledávání je povinný.");
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            var definition = new SearchFavoriteDefinition(
                favorite.Name,
                favorite.MatchQuery,
                favorite.QueryText,
                favorite.IsFuzzy);

            await _fileQueryService.AddFavoriteAsync(definition, ct);
            StatusService.Info("Oblíbené vyhledávání bylo uloženo.");
        }, "Ukládám oblíbené vyhledávání…");

        await LoadAsync();
    }
}
