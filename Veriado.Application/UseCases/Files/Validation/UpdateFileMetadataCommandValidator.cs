using Veriado.Appl.UseCases.Files.UpdateFileMetadata;

namespace Veriado.Appl.UseCases.Files.Validation;

/// <summary>
/// Validates <see cref="UpdateFileMetadataCommand"/> instances.
/// </summary>
public sealed class UpdateFileMetadataCommandValidator : AbstractValidator<UpdateFileMetadataCommand>, IRequestValidator<UpdateFileMetadataCommand>
{
    private static readonly IReadOnlyCollection<string> NoErrors = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateFileMetadataCommandValidator"/> class.
    /// </summary>
    public UpdateFileMetadataCommandValidator()
    {
        RuleFor(command => command.FileId)
            .NotEmpty();

        RuleFor(command => command.Mime)
            .Must(static mime => mime is null || !string.IsNullOrWhiteSpace(mime))
            .WithMessage("When provided, MIME must contain non-whitespace characters.");

        RuleFor(command => command.Author)
            .Must(static author => author is null || author.Trim().Length > 0)
            .WithMessage("Author must contain non-whitespace characters when provided.")
            .MaximumLength(256)
            .When(static command => command.Author is not null);
    }

    async Task<IReadOnlyCollection<string>> IRequestValidator<UpdateFileMetadataCommand>.ValidateAsync(
        UpdateFileMetadataCommand request,
        CancellationToken cancellationToken)
    {
        var result = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsValid
            ? NoErrors
            : result.Errors.Select(static failure => failure.ErrorMessage).ToArray();
    }
}
