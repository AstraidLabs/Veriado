using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Search;

namespace Veriado.Presentation.ViewModels.Files;

/// <summary>
/// Maintains the favourite search entries displayed by the UI.
/// </summary>
public sealed partial class FavoritesViewModel : ViewModelBase
{
    public FavoritesViewModel(IMessenger messenger)
        : base(messenger)
    {
        Items = new ObservableCollection<SearchFavoriteItem>();
    }

    /// <summary>
    /// Gets the favourite entries.
    /// </summary>
    public ObservableCollection<SearchFavoriteItem> Items { get; }
}
