using Veriado.Appl.UseCases.Files.CreateFile;

namespace Veriado.Appl.UseCases.Files.Validation;

/// <summary>
/// Validates <see cref="CreateFileCommand"/> instances using FluentValidation and integrates with the MediatR pipeline.
/// </summary>
public sealed class CreateFileCommandValidator : AbstractValidator<CreateFileCommand>, IRequestValidator<CreateFileCommand>
{
    private static readonly IReadOnlyCollection<string> NoErrors = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateFileCommandValidator"/> class.
    /// </summary>
    public CreateFileCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty();

        RuleFor(command => command.Extension)
            .NotEmpty();

        RuleFor(command => command.Mime)
            .NotEmpty();

        RuleFor(command => command.Author)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(command => command.Content)
            .NotNull()
            .Must(content => content.Length > 0)
            .WithMessage("Content must not be empty.");
    }

    async Task<IReadOnlyCollection<string>> IRequestValidator<CreateFileCommand>.ValidateAsync(
        CreateFileCommand request,
        CancellationToken cancellationToken)
    {
        var result = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsValid
            ? NoErrors
            : result.Errors.Select(static failure => failure.ErrorMessage).ToArray();
    }
}
