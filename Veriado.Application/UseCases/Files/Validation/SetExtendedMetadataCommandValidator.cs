using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Veriado.Application.Common;
using Veriado.Application.UseCases.Files.SetExtendedMetadata;

namespace Veriado.Application.UseCases.Files.Validation;

/// <summary>
/// Validates extended metadata updates.
/// </summary>
public sealed class SetExtendedMetadataCommandValidator : AbstractValidator<SetExtendedMetadataCommand>, IRequestValidator<SetExtendedMetadataCommand>
{
    private static readonly IReadOnlyCollection<string> NoErrors = Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="SetExtendedMetadataCommandValidator"/> class.
    /// </summary>
    public SetExtendedMetadataCommandValidator()
    {
        RuleFor(command => command.FileId)
            .NotEmpty();

        RuleFor(command => command.Entries)
            .NotNull()
            .Must(entries => entries.Count > 0)
            .WithMessage("At least one metadata entry must be provided.");

        RuleForEach(command => command.Entries)
            .SetValidator(new ExtendedMetadataEntryValidator());
    }

    async Task<IReadOnlyCollection<string>> IRequestValidator<SetExtendedMetadataCommand>.ValidateAsync(
        SetExtendedMetadataCommand request,
        CancellationToken cancellationToken)
    {
        var result = await base.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsValid
            ? NoErrors
            : result.Errors.Select(static failure => failure.ErrorMessage).ToArray();
    }

    private sealed class ExtendedMetadataEntryValidator : AbstractValidator<ExtendedMetadataEntry>
    {
        public ExtendedMetadataEntryValidator()
        {
            RuleFor(entry => entry.FormatId)
                .NotEmpty();

            RuleFor(entry => entry.PropertyId)
                .GreaterThanOrEqualTo(0);
        }
    }
}
