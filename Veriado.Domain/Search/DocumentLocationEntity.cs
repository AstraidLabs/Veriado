namespace Veriado.Domain.Search;

/// <summary>
/// Represents an optional geospatial location associated with a document.
/// </summary>
public sealed class DocumentLocationEntity
{
    /// <summary>
    /// Gets the document identifier.
    /// </summary>
    public Guid FileId { get; private set; }

    /// <summary>
    /// Gets the latitude component.
    /// </summary>
    public double Latitude { get; private set; }

    /// <summary>
    /// Gets the longitude component.
    /// </summary>
    public double Longitude { get; private set; }
}
