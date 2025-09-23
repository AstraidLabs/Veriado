using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common;

namespace Veriado.Appl.Pipeline.Idempotency;

/// <summary>
/// Prevents duplicate processing of commands by leveraging the request context.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IRequestContext _requestContext;
    private readonly IIdempotencyStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    public IdempotencyBehavior(IRequestContext requestContext, IIdempotencyStore store)
    {
        _requestContext = requestContext;
        _store = store;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestId = _requestContext.RequestId;
        if (!requestId.HasValue)
        {
            return await next();
        }

        if (!await _store.TryRegisterAsync(requestId.Value, cancellationToken))
        {
            if (TryCreateDuplicateResponse(out var duplicate))
            {
                return duplicate;
            }

            throw new InvalidOperationException($"Duplicate request '{requestId}' detected.");
        }

        try
        {
            var response = await next();
            await _store.MarkProcessedAsync(requestId.Value, cancellationToken);
            return response;
        }
        catch
        {
            await _store.MarkFailedAsync(requestId.Value, cancellationToken);
            throw;
        }
    }

    private static bool TryCreateDuplicateResponse(out TResponse response)
    {
        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(AppResult<>))
        {
            var argument = typeof(TResponse).GenericTypeArguments[0];
            var method = typeof(AppResult<>).MakeGenericType(argument).GetMethod(
                "Conflict",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);
            if (method is not null)
            {
                response = (TResponse)method.Invoke(null, new object[] { "The request has already been processed." })!;
                return true;
            }
        }

        response = default!;
        return false;
    }
}
