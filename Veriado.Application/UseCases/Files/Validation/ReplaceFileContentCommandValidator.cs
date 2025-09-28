using Veriado.Appl.UseCases.Files.ReplaceFileContent;

namespace Veriado.Appl.UseCases.Files.Validation;

/// <summary>
/// Validates <see cref="ReplaceFileContentCommand"/> instances and bridges to the request pipeline.
/// </summary>
public sealed class ReplaceFileContentCommandValidator : AbstractValidator<ReplaceFileContentCommand>, IRequestValidator<ReplaceFileContentCommand>
{
    private static readonly IReadOnlyCollection<string> NoErrors = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplaceFileContentCommandValidator"/> class.
    /// </summary>
    public ReplaceFileContentCommandValidator()
    {
        RuleFor(command => command.FileId)
            .NotEmpty();

        RuleFor(command => command.Content)
            .NotNull()
            .Must(content => content.Length > 0)
            .WithMessage("Content must not be empty.");
    }

    async Task<IReadOnlyCollection<string>> IRequestValidator<ReplaceFileContentCommand>.ValidateAsync(
        ReplaceFileContentCommand request,
        CancellationToken cancellationToken)
    {
        var result = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsValid
            ? NoErrors
            : result.Errors.Select(static failure => failure.ErrorMessage).ToArray();
    }
}
