namespace Veriado.Presentation.Messages;

/// <summary>
/// Message signalling that an import operation has completed.
/// </summary>
/// <param name="Total">The total number of processed files.</param>
/// <param name="Succeeded">The number of successfully imported files.</param>
public sealed record ImportCompletedMessage(int Total, int Succeeded);
