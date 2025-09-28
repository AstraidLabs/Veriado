namespace Veriado.Appl.Common;

/// <summary>
/// Provides guard helper methods for validating arguments.
/// </summary>
public static class Guard
{
    /// <summary>
    /// Ensures that the provided value is not <see langword="null"/>.
    /// </summary>
    public static T AgainstNull<T>(T? value, string parameterName)
        where T : class
    {
        return value ?? throw new ArgumentNullException(parameterName);
    }

    /// <summary>
    /// Ensures that the provided string is not null, empty or whitespace.
    /// </summary>
    public static string AgainstNullOrWhiteSpace(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        }

        return value.Trim();
    }

    /// <summary>
    /// Ensures that the provided integer value is greater than or equal to the minimum.
    /// </summary>
    public static int AgainstLessThan(int value, int minimum, string parameterName)
    {
        if (value < minimum)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"Value must be greater than or equal to {minimum}.");
        }

        return value;
    }

    /// <summary>
    /// Ensures that the provided long value is within range.
    /// </summary>
    public static long AgainstNegative(long value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be non-negative.");
        }

        return value;
    }
}

/// <summary>
/// Defines a validator that can validate requests before they reach the handler.
/// </summary>
/// <typeparam name="TRequest">The type of request to validate.</typeparam>
public interface IRequestValidator<in TRequest>
{
    /// <summary>
    /// Validates the provided request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collection of validation error messages. An empty collection indicates success.</returns>
    Task<IReadOnlyCollection<string>> ValidateAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Represents validation errors produced by <see cref="IRequestValidator{TRequest}"/> implementations.
/// </summary>
public sealed class ValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="errors">The validation error messages.</param>
    public ValidationException(IReadOnlyCollection<string> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    /// <summary>
    /// Gets the validation error messages.
    /// </summary>
    public IReadOnlyCollection<string> Errors { get; }
}
