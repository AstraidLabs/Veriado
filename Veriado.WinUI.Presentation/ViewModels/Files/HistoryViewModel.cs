using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Presentation.Models.Search;

namespace Veriado.Presentation.ViewModels.Files;

/// <summary>
/// Maintains the recent search history entries displayed by the UI.
/// </summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    public HistoryViewModel(IMessenger messenger)
        : base(messenger)
    {
        Items = new ObservableCollection<SearchHistoryEntryModel>();
    }

    /// <summary>
    /// Gets the history entries.
    /// </summary>
    public ObservableCollection<SearchHistoryEntryModel> Items { get; }
}
