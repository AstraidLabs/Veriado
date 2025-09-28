namespace Veriado.Mapping.AC;

/// <summary>
/// Provides guard utilities preventing unintended binary materialization.
/// </summary>
public static class Guards
{
    /// <summary>
    /// Ensures that the supplied file detail DTO does not expose binary content bytes.
    /// </summary>
    /// <param name="detail">The detail DTO.</param>
    /// <returns>The DTO with cleared binary payload.</returns>
    public static FileDetailDto WithoutContentBytes(FileDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return detail with { Content = detail.Content.WithoutBytes() };
    }

    /// <summary>
    /// Optionally attaches binary content bytes to the detail DTO.
    /// </summary>
    /// <param name="detail">The detail DTO.</param>
    /// <param name="bytes">The binary payload.</param>
    /// <param name="includeContent">Indicates whether the payload should be attached.</param>
    /// <returns>The resulting DTO.</returns>
    public static FileDetailDto WithContentBytes(FileDetailDto detail, byte[]? bytes, bool includeContent)
    {
        ArgumentNullException.ThrowIfNull(detail);
        if (!includeContent || bytes is null)
        {
            return detail with { Content = detail.Content.WithoutBytes() };
        }

        return detail with { Content = detail.Content.WithBytes(bytes) };
    }
}
