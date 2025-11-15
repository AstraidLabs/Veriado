using Veriado.Contracts.Files;

namespace Veriado.WinUI.Helpers;

internal static class ValidityGlyphProvider
{
    public static string? GetGlyph(ValidityStatus status) => status switch
    {
        ValidityStatus.Expired => "\uEA39",
        ValidityStatus.ExpiringToday => "\uE814",
        ValidityStatus.ExpiringSoon => "\uE814",
        ValidityStatus.ExpiringLater => "\uE73E",
        ValidityStatus.LongTerm => "\uE73E",
        _ => null,
    };
}
