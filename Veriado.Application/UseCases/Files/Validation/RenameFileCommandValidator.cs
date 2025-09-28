using Veriado.Appl.UseCases.Files.RenameFile;

namespace Veriado.Appl.UseCases.Files.Validation;

/// <summary>
/// Validates rename operations.
/// </summary>
public sealed class RenameFileCommandValidator : AbstractValidator<RenameFileCommand>, IRequestValidator<RenameFileCommand>
{
    private static readonly IReadOnlyCollection<string> NoErrors = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="RenameFileCommandValidator"/> class.
    /// </summary>
    public RenameFileCommandValidator()
    {
        RuleFor(command => command.FileId)
            .NotEmpty();

        RuleFor(command => command.Name)
            .NotEmpty();
    }

    async Task<IReadOnlyCollection<string>> IRequestValidator<RenameFileCommand>.ValidateAsync(
        RenameFileCommand request,
        CancellationToken cancellationToken)
    {
        var result = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsValid
            ? NoErrors
            : result.Errors.Select(static failure => failure.ErrorMessage).ToArray();
    }
}
