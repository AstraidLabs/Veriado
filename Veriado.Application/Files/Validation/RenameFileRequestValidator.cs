using System;
using FluentValidation;
using Veriado.Application.Files.Commands;

namespace Veriado.Application.Files.Validation;

/// <summary>
/// Validates <see cref="RenameFileCommand"/> instances.
/// </summary>
[Obsolete("Use Veriado.Application.UseCases.Files.Validation.RenameFileCommandValidator instead.")]
public sealed class RenameFileRequestValidator : AbstractValidator<RenameFileCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenameFileRequestValidator"/> class.
    /// </summary>
    public RenameFileRequestValidator()
    {
        RuleFor(command => command.FileId).NotEmpty();
        RuleFor(command => command.NewName).NotNull();
    }
}
