using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Veriado.Contracts.Search;
using Veriado.WinUI.Services.Messages;
using SavedViewDto = Veriado.Contracts.Search.SearchFavoriteItem;

namespace Veriado.WinUI.ViewModels.Files;

public sealed partial class FilesGridViewModel :
    IRecipient<QuerySubmittedMessage>,
    IRecipient<ApplySavedViewMessage>,
    IRecipient<SaveCurrentQueryRequestedMessage>,
    IRecipient<ClearSearchHistoryRequestedMessage>,
    IRecipient<FocusSearchRequestedMessage>
{
    public void Receive(QuerySubmittedMessage message)
    {
        if (string.IsNullOrWhiteSpace(message?.Text))
        {
            return;
        }

        SearchText = message.Text;
        _ = RefreshCommand.ExecuteAsync(null);
    }

    public void Receive(ApplySavedViewMessage message)
    {
        if (message?.Saved is null)
        {
            return;
        }

        SearchText = ResolveSearchText(message.Saved);
        _ = RefreshCommand.ExecuteAsync(null);
    }

    public void Receive(SaveCurrentQueryRequestedMessage message)
    {
        _ = SaveCurrentQueryAsync(message?.Text);
    }

    public void Receive(ClearSearchHistoryRequestedMessage message)
    {
        _ = ClearSearchHistoryAsync();
    }

    public void Receive(FocusSearchRequestedMessage message)
    {
        // Intentionally left blank. The view listens for this message to focus the search box.
    }

    private async Task SaveCurrentQueryAsync(string? requestedName)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return;
        }

        var favoriteName = string.IsNullOrWhiteSpace(requestedName)
            ? SearchText!.Trim()
            : requestedName.Trim();

        if (string.IsNullOrWhiteSpace(favoriteName))
        {
            StatusService.Error("Název uloženého filtru je povinný.");
            return;
        }

        await SafeExecuteAsync(async ct =>
        {
            var definition = new SearchFavoriteDefinition(
                favoriteName!,
                SearchText!,
                SearchText,
                false);

            await _queryService.AddFavoriteAsync(definition, ct).ConfigureAwait(false);
            StatusService.Info("Aktuální filtr byl uložen mezi oblíbené.");
        }, "Ukládám aktuální filtr…");
    }

    private async Task ClearSearchHistoryAsync()
    {
        await SafeExecuteAsync(async ct =>
        {
            await _historyService.ClearAsync(null, ct).ConfigureAwait(false);
            StatusService.Info("Historie vyhledávání byla vymazána.");
        }, "Mažu historii vyhledávání…");
    }

    private static string? ResolveSearchText(SavedViewDto saved)
    {
        if (!string.IsNullOrWhiteSpace(saved.QueryText))
        {
            return saved.QueryText;
        }

        return string.IsNullOrWhiteSpace(saved.MatchQuery)
            ? saved.Name
            : saved.MatchQuery;
    }
}
