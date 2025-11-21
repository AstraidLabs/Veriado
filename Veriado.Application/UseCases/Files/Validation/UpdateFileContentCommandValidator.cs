using System.Threading;
using System.Linq;
using FluentValidation;
using Veriado.Appl.Common;
using Veriado.Appl.UseCases.Files.UpdateFileContent;

namespace Veriado.Appl.UseCases.Files.Validation;

/// <summary>
/// Validates <see cref="UpdateFileContentCommand"/> instances.
/// </summary>
public sealed class UpdateFileContentCommandValidator
    : AbstractValidator<UpdateFileContentCommand>,
        IRequestValidator<UpdateFileContentCommand>
{
    public UpdateFileContentCommandValidator()
    {
        RuleFor(c => c.FileId).NotEmpty();
        RuleFor(c => c.SourceFileFullPath).NotEmpty();
    }

    async Task<IReadOnlyCollection<string>> IRequestValidator<UpdateFileContentCommand>.ValidateAsync(
        UpdateFileContentCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (validation.IsValid)
        {
            return Array.Empty<string>();
        }

        return validation.Errors
            .Select(e => e.ErrorMessage)
            .ToArray();
    }
}
