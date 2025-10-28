namespace Veriado.WinUI.Services.Abstractions;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel");
    Task ShowInfoAsync(string title, string message);
    Task ShowErrorAsync(string title, string message);
    Task ShowAsync(string title, UIElement content, string primaryButtonText = "OK");

    Task<DialogResult> ShowDialogAsync(DialogRequest request, CancellationToken cancellationToken = default);
}

public sealed record DialogRequest(
    string Title,
    UIElement Content,
    string PrimaryButtonText,
    string? SecondaryButtonText = null,
    string? CloseButtonText = null,
    ContentDialogButton DefaultButton = ContentDialogButton.Primary);

public enum DialogOutcome
{
    None,
    Primary,
    Secondary,
    Close,
    Canceled,
}

public readonly record struct DialogResult(DialogOutcome Outcome)
{
    public bool IsPrimary => Outcome == DialogOutcome.Primary;

    public bool IsSecondary => Outcome == DialogOutcome.Secondary;

    public bool IsClose => Outcome == DialogOutcome.Close;

    public bool IsCanceled => Outcome == DialogOutcome.Canceled;

    public static DialogResult From(ContentDialogResult result, bool wasCloseButton)
    {
        return result switch
        {
            ContentDialogResult.Primary => new DialogResult(DialogOutcome.Primary),
            ContentDialogResult.Secondary => new DialogResult(DialogOutcome.Secondary),
            _ => new DialogResult(wasCloseButton ? DialogOutcome.Close : DialogOutcome.None),
        };
    }

    public static DialogResult Canceled() => new(DialogOutcome.Canceled);
}
