using System;

namespace Veriado.Appl.Common.Exceptions;

/// <summary>
/// Represents an optimistic concurrency violation detected while persisting a file aggregate.
/// </summary>
public sealed class FileConcurrencyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileConcurrencyException"/> class with a default message.
    /// </summary>
    public FileConcurrencyException()
        : base("The file was modified by another operation. Please reload the file and try again.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileConcurrencyException"/> class with a custom message.
    /// </summary>
    public FileConcurrencyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileConcurrencyException"/> class with a custom message and inner exception.
    /// </summary>
    public FileConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
