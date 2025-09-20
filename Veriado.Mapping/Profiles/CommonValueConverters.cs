using System;
using AutoMapper;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Mapping.Profiles;

/// <summary>
/// Provides reusable AutoMapper value converters for domain primitives.
/// </summary>
public static class CommonValueConverters
{
    public sealed class FileNameToStringConverter : IValueConverter<FileName, string>
    {
        public string Convert(FileName sourceMember, ResolutionContext context) => sourceMember.Value;
    }

    public sealed class FileExtensionToStringConverter : IValueConverter<FileExtension, string>
    {
        public string Convert(FileExtension sourceMember, ResolutionContext context) => sourceMember.Value;
    }

    public sealed class MimeTypeToStringConverter : IValueConverter<MimeType, string>
    {
        public string Convert(MimeType sourceMember, ResolutionContext context) => sourceMember.Value;
    }

    public sealed class ByteSizeToLongConverter : IValueConverter<ByteSize, long>
    {
        public long Convert(ByteSize sourceMember, ResolutionContext context) => sourceMember.Value;
    }

    public sealed class UtcTimestampToDateTimeOffsetConverter : IValueConverter<UtcTimestamp, DateTimeOffset>
    {
        public DateTimeOffset Convert(UtcTimestamp sourceMember, ResolutionContext context) => sourceMember.Value;
    }

    public sealed class NullableUtcTimestampToNullableDateTimeOffsetConverter : IValueConverter<UtcTimestamp?, DateTimeOffset?>
    {
        public DateTimeOffset? Convert(UtcTimestamp? sourceMember, ResolutionContext context) => sourceMember?.Value;
    }

    public sealed class FileHashToStringConverter : IValueConverter<FileHash, string>
    {
        public string Convert(FileHash sourceMember, ResolutionContext context) => sourceMember.Value;
    }

    public sealed class FileAttributesToIntConverter : IValueConverter<FileAttributesFlags, int>
    {
        public int Convert(FileAttributesFlags sourceMember, ResolutionContext context) => (int)sourceMember;
    }
}
