using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.WinUI.Messages;

namespace Veriado.WinUI.ViewModels.Files;

/// <summary>
/// Represents the search bar state and commands used on the files page.
/// </summary>
public sealed partial class SearchBarViewModel : ViewModelBase
{
    public SearchBarViewModel(IMessenger messenger)
        : base(messenger)
    {
        Suggestions = new ObservableCollection<string>();
        Tokens = new ObservableCollection<string>();
    }

    /// <summary>
    /// Gets the suggestions displayed by the auto-suggest box.
    /// </summary>
    public ObservableCollection<string> Suggestions { get; }

    /// <summary>
    /// Gets the advanced filter tokens displayed by the tokenizing text box.
    /// </summary>
    public ObservableCollection<string> Tokens { get; }

    [ObservableProperty]
    private string? query;

    [RelayCommand]
    private void ApplySuggestion(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        Query = suggestion;
        Messenger.Send(new SearchRequestedMessage(Query));
    }

    [RelayCommand]
    private void Submit(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Query = text;
        }

        Messenger.Send(new SearchRequestedMessage(Query));
    }

    /// <summary>
    /// Clears the current query and tokens.
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        Query = null;
        Tokens.Clear();
        Messenger.Send(new SearchRequestedMessage(Query));
    }
}
