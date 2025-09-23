using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Veriado.Appl.Common;
using Veriado.Appl.UseCases.Files.ClearFileValidity;

namespace Veriado.Appl.UseCases.Files.Validation;

/// <summary>
/// Validates commands clearing document validity information.
/// </summary>
public sealed class ClearFileValidityCommandValidator : AbstractValidator<ClearFileValidityCommand>, IRequestValidator<ClearFileValidityCommand>
{
    private static readonly IReadOnlyCollection<string> NoErrors = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ClearFileValidityCommandValidator"/> class.
    /// </summary>
    public ClearFileValidityCommandValidator()
    {
        RuleFor(command => command.FileId)
            .NotEmpty();
    }

    async Task<IReadOnlyCollection<string>> IRequestValidator<ClearFileValidityCommand>.ValidateAsync(
        ClearFileValidityCommand request,
        CancellationToken cancellationToken)
    {
        var result = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsValid
            ? NoErrors
            : result.Errors.Select(static failure => failure.ErrorMessage).ToArray();
    }
}
