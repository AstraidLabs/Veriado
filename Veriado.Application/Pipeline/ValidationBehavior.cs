using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Application.Common;

namespace Veriado.Application.Pipeline;

/// <summary>
/// Executes registered validators prior to invoking the request handler.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IRequestValidator<TRequest>> _validators;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    public ValidationBehavior(IEnumerable<IRequestValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var errors = new List<string>();
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            if (result.Count > 0)
            {
                errors.AddRange(result);
            }
        }

        if (errors.Count == 0)
        {
            return await next();
        }

        var readOnly = errors.AsReadOnly();
        if (TryCreateValidationResponse(readOnly, out var response))
        {
            return response;
        }

        throw new ValidationException(readOnly);
    }

    private static bool TryCreateValidationResponse(IReadOnlyCollection<string> errors, out TResponse response)
    {
        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(AppResult<>))
        {
            var argument = typeof(TResponse).GenericTypeArguments[0];
            var method = typeof(AppResult<>).MakeGenericType(argument).GetMethod(
                "Validation",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(IReadOnlyCollection<string>) },
                modifiers: null);
            if (method is not null)
            {
                response = (TResponse)method.Invoke(null, new object[] { errors })!;
                return true;
            }
        }

        response = default!;
        return false;
    }
}
