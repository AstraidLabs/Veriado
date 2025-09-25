using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Veriado.WinUI.Services.Abstractions;

public interface IFilesSearchSuggestionsProvider
{
    Task<IReadOnlyList<string>> GetSuggestionsAsync(CancellationToken cancellationToken);
}
