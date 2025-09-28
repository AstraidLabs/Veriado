namespace Veriado.WinUI.Services.Abstractions;

public interface IFilesSearchSuggestionsProvider
{
    Task<IReadOnlyList<string>> GetSuggestionsAsync(CancellationToken cancellationToken);
}
