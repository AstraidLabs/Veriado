using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Veriado.Application.Common;
using Veriado.Application.UseCases.Files.ApplySystemMetadata;

namespace Veriado.Application.UseCases.Files.Validation;

/// <summary>
/// Validates <see cref="ApplySystemMetadataCommand"/> requests.
/// </summary>
public sealed class ApplySystemMetadataCommandValidator : AbstractValidator<ApplySystemMetadataCommand>, IRequestValidator<ApplySystemMetadataCommand>
{
    private static readonly IReadOnlyCollection<string> NoErrors = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplySystemMetadataCommandValidator"/> class.
    /// </summary>
    public ApplySystemMetadataCommandValidator()
    {
        RuleFor(command => command.FileId)
            .NotEmpty();
    }

    async Task<IReadOnlyCollection<string>> IRequestValidator<ApplySystemMetadataCommand>.ValidateAsync(
        ApplySystemMetadataCommand request,
        CancellationToken cancellationToken)
    {
        var result = await base.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsValid
            ? NoErrors
            : result.Errors.Select(static failure => failure.ErrorMessage).ToArray();
    }
}
