using System.Collections.ObjectModel;
using Veriado.Contracts.Files;
using Veriado.Services.Files;
using Veriado.WinUI.ViewModels.Base;

namespace Veriado.WinUI.ViewModels.Files;

public partial class FilesPageViewModel : ViewModelBase
{
    private readonly IFileQueryService _fileQueryService;

    public FilesPageViewModel(
        IFileQueryService fileQueryService,
        IMessenger messenger,
        IStatusService statusService,
        IDispatcherService dispatcher,
        IExceptionHandler exceptionHandler)
        : base(messenger, statusService, dispatcher, exceptionHandler)
    {
        _fileQueryService = fileQueryService ?? throw new ArgumentNullException(nameof(fileQueryService));
        Items = new ObservableCollection<FileSummaryDto>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    [ObservableProperty]
    private string? searchText;

    public ObservableCollection<FileSummaryDto> Items { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    private Task RefreshAsync()
    {
        return SafeExecuteAsync(async cancellationToken =>
        {
            var query = new FileGridQueryDto
            {
                Text = SearchText,
                Page = 1,
                PageSize = 50,
            };

            var result = await _fileQueryService.GetGridAsync(query, cancellationToken).ConfigureAwait(false);

            await Dispatcher.Enqueue(() =>
            {
                Items.Clear();
                foreach (var item in result.Items)
                {
                    Items.Add(item);
                }
            }).ConfigureAwait(false);

            StatusService.Info($"Načteno {Items.Count} položek.");
        });
    }
}
