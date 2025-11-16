namespace Veriado.Contracts.Files;

/// <summary>
/// Describes the available high-level validity filter modes.
/// </summary>
public enum ValidityFilterMode
{
    None = 0,
    HasValidity,
    NoValidity,
    CurrentlyValid,
    Expired,
    ExpiringWithin,
    ExpiringRange,
}
