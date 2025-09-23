using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Veriado.Contracts.Search;
using Veriado.Mappers;
using Veriado.Models.Search;
using Veriado.Services.Files;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Search;

public sealed partial class FavoritesViewModel : ViewModelBase
{
    private readonly IFileQueryService _fileQueryService;

    public FavoritesViewModel(IFileQueryService fileQueryService)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
    }

    public ObservableCollection<SearchFavoriteItemModel> Items { get; } = new();

    [RelayCommand]
    private async Task LoadAsync()
    {
        await SafeExecuteAsync(async ct =>
        {
            var favorites = await _fileQueryService.GetFavoritesAsync(ct).ConfigureAwait(false);

            Items.Clear();
            foreach (var favorite in favorites)
            {
                Items.Add(favorite.ToSearchFavoriteItemModel());
            }

            StatusMessage = Items.Count == 0
                ? "Žádné uložené oblíbené položky."
                : $"Načteno {Items.Count} oblíbených vyhledávání.";
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
            await _fileQueryService.RemoveFavoriteAsync(id, ct).ConfigureAwait(false);
        }, "Odstraňuji oblíbené vyhledávání…");

        await LoadAsync();
    }

    [RelayCommand]
    private async Task AddAsync(SearchFavoriteDefinitionModel? favorite)
    {
        if (favorite is null || string.IsNullOrWhiteSpace(favorite.Name))
        {
            HasError = true;
            StatusMessage = "Název oblíbeného vyhledávání je povinný.";
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            var definition = new SearchFavoriteDefinition(
                favorite.Name,
                favorite.MatchQuery,
                favorite.QueryText,
                favorite.IsFuzzy);

            await _fileQueryService.AddFavoriteAsync(definition, ct).ConfigureAwait(false);
        }, "Ukládám oblíbené vyhledávání…");

        await LoadAsync();
    }
}
