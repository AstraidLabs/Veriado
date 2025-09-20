using System;
using FluentValidation;
using Veriado.Application.Files.Commands;

namespace Veriado.Application.Files.Validation;

/// <summary>
/// Validates <see cref="ReplaceContentCommand"/> instances.
/// </summary>
public sealed class ReplaceContentRequestValidator : AbstractValidator<ReplaceContentCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReplaceContentRequestValidator"/> class.
    /// </summary>
    public ReplaceContentRequestValidator()
    {
        RuleFor(command => command.FileId).NotEmpty();
        RuleFor(command => command.ContentBytes)
            .NotNull()
            .Must(bytes => bytes.Length > 0)
            .WithMessage("Content must not be empty.");
        RuleFor(command => command.MaxContentLength)
            .GreaterThan(0)
            .When(command => command.MaxContentLength.HasValue);
    }
}
