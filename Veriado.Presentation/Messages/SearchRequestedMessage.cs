namespace Veriado.Presentation.Messages;

/// <summary>
/// Message requesting execution of a search with the supplied query text.
/// </summary>
/// <param name="Query">The query text.</param>
public sealed record SearchRequestedMessage(string? Query);
