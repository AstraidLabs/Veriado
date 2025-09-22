using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Presentation.Models.Search;

namespace Veriado.Presentation.ViewModels.Files;

/// <summary>
/// Maintains the favourite search entries displayed by the UI.
/// </summary>
public sealed partial class FavoritesViewModel : ViewModelBase
{
    public FavoritesViewModel(IMessenger messenger)
        : base(messenger)
    {
        Items = new ObservableCollection<SearchFavoriteItemModel>();
    }

    /// <summary>
    /// Gets the favourite entries.
    /// </summary>
    public ObservableCollection<SearchFavoriteItemModel> Items { get; }
}
