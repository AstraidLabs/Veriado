using Veriado.Appl.UseCases.Files.DeleteFile;

namespace Veriado.Appl.UseCases.Files.Validation;

/// <summary>
/// Validates delete file commands.
/// </summary>
public sealed class DeleteFileCommandValidator : AbstractValidator<DeleteFileCommand>, IRequestValidator<DeleteFileCommand>
{
    private static readonly IReadOnlyCollection<string> NoErrors = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteFileCommandValidator"/> class.
    /// </summary>
    public DeleteFileCommandValidator()
    {
        RuleFor(command => command.FileId)
            .NotEmpty();
    }

    async Task<IReadOnlyCollection<string>> IRequestValidator<DeleteFileCommand>.ValidateAsync(
        DeleteFileCommand request,
        CancellationToken cancellationToken)
    {
        var result = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsValid
            ? NoErrors
            : result.Errors.Select(static failure => failure.ErrorMessage).ToArray();
    }
}
