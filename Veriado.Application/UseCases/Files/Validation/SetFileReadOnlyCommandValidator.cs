using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Veriado.Application.Common;
using Veriado.Application.UseCases.Files.SetFileReadOnly;

namespace Veriado.Application.UseCases.Files.Validation;

/// <summary>
/// Validates read-only status updates.
/// </summary>
public sealed class SetFileReadOnlyCommandValidator : AbstractValidator<SetFileReadOnlyCommand>, IRequestValidator<SetFileReadOnlyCommand>
{
    private static readonly IReadOnlyCollection<string> NoErrors = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="SetFileReadOnlyCommandValidator"/> class.
    /// </summary>
    public SetFileReadOnlyCommandValidator()
    {
        RuleFor(command => command.FileId)
            .NotEmpty();
    }

    async Task<IReadOnlyCollection<string>> IRequestValidator<SetFileReadOnlyCommand>.ValidateAsync(
        SetFileReadOnlyCommand request,
        CancellationToken cancellationToken)
    {
        var result = await base.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsValid
            ? NoErrors
            : result.Errors.Select(static failure => failure.ErrorMessage).ToArray();
    }
}
