using SavedViewDto = Veriado.Contracts.Search.SearchFavoriteItem;

namespace Veriado.WinUI.Services.Messages;

public sealed record QuerySubmittedMessage(string Text);

public sealed record ApplySavedViewMessage(SavedViewDto Saved);

public sealed record SaveCurrentQueryRequestedMessage(string? Text);

public sealed record ClearSearchHistoryRequestedMessage();

public sealed record FocusSearchRequestedMessage;
