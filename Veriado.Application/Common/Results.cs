using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Veriado.Appl.Common.Exceptions;

namespace Veriado.Appl.Common;

/// <summary>
/// Represents the result of an application-layer operation.
/// </summary>
/// <typeparam name="T">The type of value returned on success.</typeparam>
public readonly struct AppResult<T>
{
    private const string DefaultDatabaseErrorMessage = "A database error occurred while processing the request.";

    private readonly T? _value;
    private readonly AppError? _error;

    private AppResult(bool isSuccess, T? value, AppError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the value produced by the operation when it succeeded.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    /// <summary>
    /// Gets the application error for failed results.
    /// </summary>
    public AppError Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("Cannot access the error of a successful result.");

    /// <summary>
    /// Tries to get the value and returns a boolean indicating whether it is present.
    /// </summary>
    /// <param name="value">When the operation succeeds, contains the result value.</param>
    /// <returns><see langword="true"/> when the operation succeeded; otherwise <see langword="false"/>.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = IsSuccess ? _value : default;
        return IsSuccess;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="value">The value of the operation.</param>
    /// <returns>The successful result.</returns>
    public static AppResult<T> Success(T value) => new(true, value, null);

    /// <summary>
    /// Creates a failed result with the specified application error.
    /// </summary>
    /// <param name="error">The application error.</param>
    /// <returns>The failure result.</returns>
    public static AppResult<T> Failure(AppError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new AppResult<T>(false, default, error);
    }

    /// <summary>
    /// Creates a validation failure result with the provided error messages.
    /// </summary>
    /// <param name="errors">The validation error messages.</param>
    /// <returns>The failure result.</returns>
    public static AppResult<T> Validation(IReadOnlyCollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return Failure(AppError.Validation("Validation failed.", errors));
    }

    /// <summary>
    /// Creates a conflict result with the provided message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>The failure result.</returns>
    public static AppResult<T> Conflict(string message) => Failure(AppError.Conflict(message));

    /// <summary>
    /// Creates a not-found result with the provided message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>The failure result.</returns>
    public static AppResult<T> NotFound(string message) => Failure(AppError.NotFound(message));

    /// <summary>
    /// Creates a forbidden result with the provided message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>The failure result.</returns>
    public static AppResult<T> Forbidden(string message) => Failure(AppError.Forbidden(message));

    /// <summary>
    /// Creates a result indicating that the payload was too large.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>The failure result.</returns>
    public static AppResult<T> TooLarge(string message) => Failure(AppError.TooLarge(message));

    /// <summary>
    /// Creates an unexpected failure result.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>The failure result.</returns>
    public static AppResult<T> Unexpected(string message) => Failure(AppError.Unexpected(message));

    /// <summary>
    /// Converts an exception raised by the domain or infrastructure into an application result.
    /// </summary>
    /// <param name="exception">The exception to convert.</param>
    /// <param name="defaultMessage">An optional override message.</param>
    /// <returns>The failure result.</returns>
    public static AppResult<T> FromException(Exception exception, string? defaultMessage = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (TryExtractSqliteException(exception, out var sqliteException))
        {
            var message = defaultMessage ?? DefaultDatabaseErrorMessage;
            return Failure(AppError.Database(message, sqliteException.Message));
        }

        return exception switch
        {
            ArgumentException or ArgumentNullException or ArgumentOutOfRangeException
                => Failure(AppError.Validation(defaultMessage ?? exception.Message, new[] { exception.Message })),
            FileConcurrencyException concurrency
                => Failure(AppError.Conflict(defaultMessage ?? concurrency.Message)),
            InvalidOperationException
                => Failure(AppError.Conflict(defaultMessage ?? exception.Message)),
            _ => Failure(AppError.Unexpected(defaultMessage ?? "An unexpected error occurred.")),
        };
    }

    private static bool TryExtractSqliteException(Exception exception, out SqliteException sqliteException)
    {
        switch (exception)
        {
            case SqliteException direct:
                sqliteException = direct;
                return true;
            case AggregateException aggregate:
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (TryExtractSqliteException(inner, out sqliteException))
                    {
                        return true;
                    }
                }
                break;
            case { InnerException: { } inner }:
                return TryExtractSqliteException(inner, out sqliteException);
        }

        sqliteException = null!;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => IsSuccess
        ? $"Success({Value})"
        : $"Failure({Error.Code}: {Error.Message})";
}

/// <summary>
/// Represents an application error with a standardized error code.
/// </summary>
/// <param name="Code">The error code.</param>
/// <param name="Message">The human-readable error message.</param>
/// <param name="Details">Optional detailed messages.</param>
public sealed record AppError(ErrorCode Code, string Message, IReadOnlyCollection<string>? Details = null)
{
    /// <summary>
    /// Creates a not-found error.
    /// </summary>
    public static AppError NotFound(string message) => new(ErrorCode.NotFound, message);

    /// <summary>
    /// Creates a conflict error.
    /// </summary>
    public static AppError Conflict(string message) => new(ErrorCode.Conflict, message);

    /// <summary>
    /// Creates a validation error.
    /// </summary>
    public static AppError Validation(string message, IReadOnlyCollection<string>? details = null) => new(ErrorCode.Validation, message, details);

    /// <summary>
    /// Creates a forbidden error.
    /// </summary>
    public static AppError Forbidden(string message) => new(ErrorCode.Forbidden, message);

    /// <summary>
    /// Creates a payload-too-large error.
    /// </summary>
    public static AppError TooLarge(string message) => new(ErrorCode.TooLarge, message);

    /// <summary>
    /// Creates a database error.
    /// </summary>
    public static AppError Database(string message, string? detail = null)
    {
        IReadOnlyCollection<string>? details = detail is { Length: > 0 }
            ? new[] { detail }
            : null;

        return new AppError(ErrorCode.Database, message, details);
    }

    /// <summary>
    /// Creates an unexpected error.
    /// </summary>
    public static AppError Unexpected(string message) => new(ErrorCode.Unexpected, message);
}

/// <summary>
/// Enumerates standardized error codes used by the application layer.
/// </summary>
public enum ErrorCode
{
    /// <summary>
    /// Represents a missing resource.
    /// </summary>
    NotFound,

    /// <summary>
    /// Represents a conflict or concurrency violation.
    /// </summary>
    Conflict,

    /// <summary>
    /// Represents validation errors.
    /// </summary>
    Validation,

    /// <summary>
    /// Represents lack of authorization to perform an operation.
    /// </summary>
    Forbidden,

    /// <summary>
    /// Represents that the payload exceeds allowed limits.
    /// </summary>
    TooLarge,

    /// <summary>
    /// Represents a database-related failure.
    /// </summary>
    Database,

    /// <summary>
    /// Represents an unexpected failure.
    /// </summary>
    Unexpected,
}
