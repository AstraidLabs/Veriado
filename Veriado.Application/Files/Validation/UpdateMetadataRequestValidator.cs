using System;
using FluentValidation;
using Veriado.Application.Files.Commands;

namespace Veriado.Application.Files.Validation;

/// <summary>
/// Validates <see cref="UpdateMetadataCommand"/> instances.
/// </summary>
[Obsolete("Use Veriado.Application.UseCases.Files.Validation.UpdateFileMetadataCommandValidator instead.")]
public sealed class UpdateMetadataRequestValidator : AbstractValidator<UpdateMetadataCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateMetadataRequestValidator"/> class.
    /// </summary>
    public UpdateMetadataRequestValidator()
    {
        RuleFor(command => command.FileId).NotEmpty();
        RuleFor(command => command.Author)
            .MaximumLength(256)
            .When(command => command.Author is not null);
        RuleFor(command => command)
            .Must(HasAnyChange)
            .WithMessage("At least one metadata change must be provided.");
    }

    private static bool HasAnyChange(UpdateMetadataCommand command)
    {
        if (command.Mime.HasValue)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(command.Author))
        {
            return true;
        }

        if (command.IsReadOnly.HasValue)
        {
            return true;
        }

        if (command.SystemMetadata.HasValue)
        {
            return true;
        }

        return command.ExtendedMetadata.Count > 0;
    }
}
