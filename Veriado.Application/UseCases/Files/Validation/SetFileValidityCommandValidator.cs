using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Veriado.Application.Common;
using Veriado.Application.UseCases.Files.SetFileValidity;

namespace Veriado.Application.UseCases.Files.Validation;

/// <summary>
/// Validates validity metadata updates.
/// </summary>
public sealed class SetFileValidityCommandValidator : AbstractValidator<SetFileValidityCommand>, IRequestValidator<SetFileValidityCommand>
{
    private static readonly IReadOnlyCollection<string> NoErrors = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="SetFileValidityCommandValidator"/> class.
    /// </summary>
    public SetFileValidityCommandValidator()
    {
        RuleFor(command => command.FileId)
            .NotEmpty();

        RuleFor(command => command.ValidUntilUtc)
            .GreaterThanOrEqualTo(command => command.IssuedAtUtc)
            .WithMessage("Valid-until must be greater than or equal to issued-at.");
    }

    async Task<IReadOnlyCollection<string>> IRequestValidator<SetFileValidityCommand>.ValidateAsync(
        SetFileValidityCommand request,
        CancellationToken cancellationToken)
    {
        var result = await base.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsValid
            ? NoErrors
            : result.Errors.Select(static failure => failure.ErrorMessage).ToArray();
    }
}
