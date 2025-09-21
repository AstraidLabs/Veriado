using System;
using FluentValidation;
using Veriado.Application.Files.Commands;

namespace Veriado.Application.Files.Validation;

/// <summary>
/// Validates <see cref="SetValidityCommand"/> instances.
/// </summary>
[Obsolete("Use Veriado.Application.UseCases.Files.Validation.SetFileValidityCommandValidator instead.")]
public sealed class SetValidityRequestValidator : AbstractValidator<SetValidityCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetValidityRequestValidator"/> class.
    /// </summary>
    public SetValidityRequestValidator()
    {
        RuleFor(command => command.FileId).NotEmpty();
        RuleFor(command => command)
            .Must(command => command.ValidUntil.Value >= command.IssuedAt.Value)
            .WithMessage("Valid-until must be greater than or equal to issued-at.");
    }
}
