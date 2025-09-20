using System;
using FluentValidation;
using Veriado.Application.Files.Commands;

namespace Veriado.Application.Files.Validation;

/// <summary>
/// Validates <see cref="ClearValidityCommand"/> instances.
/// </summary>
public sealed class ClearValidityRequestValidator : AbstractValidator<ClearValidityCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClearValidityRequestValidator"/> class.
    /// </summary>
    public ClearValidityRequestValidator()
    {
        RuleFor(command => command.FileId).NotEmpty();
    }
}
