using System;
using System.Collections.Generic;
using System.Linq;

namespace Veriado.Contracts.Common;

/// <summary>
/// Represents a standardized API response with optional data and error payloads.
/// </summary>
/// <typeparam name="T">The response data type.</typeparam>
public sealed class ApiResponse<T>
{
    private ApiResponse(bool isSuccess, T? data, IReadOnlyList<ApiError> errors)
    {
        IsSuccess = isSuccess;
        Data = data;
        Errors = errors;
    }

    /// <summary>
    /// Gets a value indicating whether the response is successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the response payload.
    /// </summary>
    public T? Data { get; }

    /// <summary>
    /// Gets the errors associated with the response.
    /// </summary>
    public IReadOnlyList<ApiError> Errors { get; }

    /// <summary>
    /// Creates a successful response.
    /// </summary>
    /// <param name="data">The response payload.</param>
    /// <returns>The created response.</returns>
    public static ApiResponse<T> Success(T data) => new(true, data, Array.Empty<ApiError>());

    /// <summary>
    /// Creates a failure response with the supplied errors.
    /// </summary>
    /// <param name="errors">The errors to attach to the response.</param>
    /// <returns>The failure response.</returns>
    public static ApiResponse<T> Failure(IEnumerable<ApiError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var materialized = errors.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("Failure response must contain at least one error.", nameof(errors));
        }

        return new ApiResponse<T>(false, default, materialized);
    }

    /// <summary>
    /// Creates a failure response from a single error.
    /// </summary>
    /// <param name="error">The error to attach.</param>
    /// <returns>The failure response.</returns>
    public static ApiResponse<T> Failure(ApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ApiResponse<T>(false, default, new[] { error });
    }
}

/// <summary>
/// Represents a non-generic API response suitable for commands without return data.
/// </summary>
public sealed class ApiResponse
{
    private ApiResponse(bool isSuccess, IReadOnlyList<ApiError> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    /// <summary>
    /// Gets a value indicating whether the response is successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the errors associated with the response.
    /// </summary>
    public IReadOnlyList<ApiError> Errors { get; }

    /// <summary>
    /// Creates a successful response without a payload.
    /// </summary>
    /// <returns>The created response.</returns>
    public static ApiResponse Success() => new(true, Array.Empty<ApiError>());

    /// <summary>
    /// Creates a failure response with the provided errors.
    /// </summary>
    /// <param name="errors">The errors to attach.</param>
    /// <returns>The created failure response.</returns>
    public static ApiResponse Failure(IEnumerable<ApiError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var materialized = errors.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("Failure response must contain at least one error.", nameof(errors));
        }

        return new ApiResponse(false, materialized);
    }

    /// <summary>
    /// Creates a failure response with a single error.
    /// </summary>
    /// <param name="error">The error to attach.</param>
    /// <returns>The created failure response.</returns>
    public static ApiResponse Failure(ApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ApiResponse(false, new[] { error });
    }
}

/// <summary>
/// Represents a structured API error entry.
/// </summary>
public sealed record ApiError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiError"/> record.
    /// </summary>
    /// <param name="code">The machine-readable error code.</param>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="target">An optional target identifying the failing input.</param>
    /// <param name="details">Optional structured error details.</param>
    public ApiError(string code, string message, string? target = null, IReadOnlyDictionary<string, string[]>? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
        Target = target;
        Details = details ?? new Dictionary<string, string[]>();
    }

    /// <summary>
    /// Gets the machine-readable error code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the optional target describing the invalid input member.
    /// </summary>
    public string? Target { get; }

    /// <summary>
    /// Gets the optional structured error details.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Details { get; }

    /// <summary>
    /// Creates an error describing an invalid value for the specified target.
    /// </summary>
    /// <param name="target">The property or parameter name.</param>
    /// <param name="message">The associated message.</param>
    /// <returns>The created error.</returns>
    public static ApiError ForValue(string target, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new ApiError("invalid_value", message, target);
    }

    /// <summary>
    /// Creates an error describing a missing required value.
    /// </summary>
    /// <param name="target">The missing value target.</param>
    /// <returns>The created error.</returns>
    public static ApiError MissingValue(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return new ApiError("missing_value", $"The value for '{target}' is required.", target);
    }
}
