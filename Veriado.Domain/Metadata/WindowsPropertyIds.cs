using System;

namespace Veriado.Domain.Metadata;

/// <summary>
/// Provides predefined Windows property keys for common document metadata fields.
/// </summary>
public static class WindowsPropertyIds
{
    private static readonly Guid SummaryInformationFormatId = Guid.Parse("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
    private static readonly Guid DocumentSummaryInformationFormatId = Guid.Parse("D5CDD502-2E9C-101B-9397-08002B2CF9AE");

    /// <summary>
    /// Gets the property key for the document title.
    /// </summary>
    public static PropertyKey Title { get; } = new(SummaryInformationFormatId, 0x00000002);

    /// <summary>
    /// Gets the property key for the document subject.
    /// </summary>
    public static PropertyKey Subject { get; } = new(SummaryInformationFormatId, 0x00000003);

    /// <summary>
    /// Gets the property key for the document author.
    /// </summary>
    public static PropertyKey Author { get; } = new(SummaryInformationFormatId, 0x00000004);

    /// <summary>
    /// Gets the property key for the document comments.
    /// </summary>
    public static PropertyKey Comments { get; } = new(SummaryInformationFormatId, 0x00000006);

    /// <summary>
    /// Gets the property key for the template name.
    /// </summary>
    public static PropertyKey Template { get; } = new(SummaryInformationFormatId, 0x00000007);

    /// <summary>
    /// Gets the property key for the last author.
    /// </summary>
    public static PropertyKey LastAuthor { get; } = new(SummaryInformationFormatId, 0x00000008);

    /// <summary>
    /// Gets the property key for the revision number.
    /// </summary>
    public static PropertyKey RevisionNumber { get; } = new(SummaryInformationFormatId, 0x00000009);

    /// <summary>
    /// Gets the property key for the document category.
    /// </summary>
    public static PropertyKey Category { get; } = new(DocumentSummaryInformationFormatId, 0x00000002);

    /// <summary>
    /// Gets the property key for the document manager.
    /// </summary>
    public static PropertyKey Manager { get; } = new(DocumentSummaryInformationFormatId, 0x0000000E);

    /// <summary>
    /// Gets the property key for the document company.
    /// </summary>
    public static PropertyKey Company { get; } = new(DocumentSummaryInformationFormatId, 0x0000000F);
}
