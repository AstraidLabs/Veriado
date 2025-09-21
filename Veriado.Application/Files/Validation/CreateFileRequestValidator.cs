using System;
using FluentValidation;
using Veriado.Application.Files.Commands;

namespace Veriado.Application.Files.Validation;

/// <summary>
/// Validates <see cref="CreateFileCommand"/> instances.
/// </summary>
[Obsolete("Use Veriado.Application.UseCases.Files.Validation.CreateFileCommandValidator instead.")]
public sealed class CreateFileRequestValidator : AbstractValidator<CreateFileCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateFileRequestValidator"/> class.
    /// </summary>
    public CreateFileRequestValidator()
    {
        RuleFor(command => command.Name).NotNull();
        RuleFor(command => command.Extension).NotNull();
        RuleFor(command => command.Mime).NotNull();
        RuleFor(command => command.Author)
            .NotEmpty()
            .MaximumLength(256);
        RuleFor(command => command.ContentBytes)
            .NotNull()
            .Must(bytes => bytes.Length > 0)
            .WithMessage("Content must not be empty.");
        RuleFor(command => command.MaxContentLength)
            .GreaterThan(0)
            .When(command => command.MaxContentLength.HasValue);
    }
}
