using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Veriado.Appl.Common;

namespace Veriado.Appl.UseCases.Queries.FileGrid;

/// <summary>
/// Integrates <see cref="FileGridQueryValidator"/> with the request validation pipeline.
/// </summary>
public sealed class FileGridQueryRequestValidator : IRequestValidator<FileGridQuery>
{
    private readonly FileGridQueryValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileGridQueryRequestValidator"/> class.
    /// </summary>
    public FileGridQueryRequestValidator(FileGridQueryValidator validator)
    {
        _validator = validator;
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<string>> ValidateAsync(FileGridQuery request, CancellationToken cancellationToken)
    {
        var result = _validator.Validate(request.Parameters);
        if (result.IsValid)
        {
            return Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
        }

        var errors = new List<string>(result.Errors.Count);
        foreach (var failure in result.Errors)
        {
            errors.Add(failure.ErrorMessage);
        }

        return Task.FromResult<IReadOnlyCollection<string>>(errors);
    }
}
