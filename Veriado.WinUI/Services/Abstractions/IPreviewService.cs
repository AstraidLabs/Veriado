namespace Veriado.WinUI.Services.Abstractions;

public sealed record PreviewResult(string? TextSnippet, byte[]? ThumbnailBytes);

public interface IPreviewService
{
    Task<PreviewResult?> GetPreviewAsync(Guid fileId, CancellationToken cancellationToken = default);
}
