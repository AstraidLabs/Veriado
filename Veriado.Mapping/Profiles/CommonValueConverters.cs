using System;
using AutoMapper;
using Veriado.Domain.Metadata;
using Veriado.Domain.ValueObjects;

namespace Veriado.Mapping.Profiles;

/// <summary>
/// Provides reusable AutoMapper value converters for domain primitives and value objects.
/// </summary>
public static class CommonValueConverters
{
    /// <summary>
    /// Registers the shared converters on the provided profile.
    /// </summary>
    /// <param name="profile">The profile on which to register the converters.</param>
    public static void Register(IProfileExpression profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.CreateMap<FileName, string>().ConvertUsing<FileNameToStringConverter>();
        profile.CreateMap<FileExtension, string>().ConvertUsing<FileExtensionToStringConverter>();
        profile.CreateMap<MimeType, string>().ConvertUsing<MimeTypeToStringConverter>();
        profile.CreateMap<FileHash, string>().ConvertUsing<FileHashToStringConverter>();
        profile.CreateMap<ByteSize, long>().ConvertUsing<ByteSizeToLongConverter>();
        profile.CreateMap<FileAttributesFlags, int>().ConvertUsing<FileAttributesToIntConverter>();

        profile.CreateMap<UtcTimestamp, DateTimeOffset>().ConvertUsing<UtcTimestampToDateTimeOffsetConverter>();
        profile.CreateMap<UtcTimestamp?, DateTimeOffset?>().ConvertUsing<NullableUtcTimestampToNullableDateTimeOffsetConverter>();
        profile.CreateMap<DateTimeOffset, UtcTimestamp>().ConvertUsing<DateTimeOffsetToUtcTimestampConverter>();
        profile.CreateMap<DateTimeOffset?, UtcTimestamp?>().ConvertUsing<NullableDateTimeOffsetToNullableUtcTimestampConverter>();
        profile.CreateMap<int, FileAttributesFlags>().ConvertUsing<IntToFileAttributesFlagsConverter>();
    }

    public sealed class FileNameToStringConverter : IValueConverter<FileName, string>
    {
        public string Convert(FileName sourceMember, ResolutionContext context) => sourceMember.Value ?? string.Empty;
    }

    public sealed class FileExtensionToStringConverter : IValueConverter<FileExtension, string>
    {
        public string Convert(FileExtension sourceMember, ResolutionContext context) => sourceMember.Value ?? string.Empty;
    }

    public sealed class MimeTypeToStringConverter : IValueConverter<MimeType, string>
    {
        public string Convert(MimeType sourceMember, ResolutionContext context) => sourceMember.Value ?? string.Empty;
    }

    public sealed class FileHashToStringConverter : IValueConverter<FileHash, string>
    {
        public string Convert(FileHash sourceMember, ResolutionContext context) => sourceMember.Value ?? string.Empty;
    }

    public sealed class ByteSizeToLongConverter : IValueConverter<ByteSize, long>
    {
        public long Convert(ByteSize sourceMember, ResolutionContext context) => sourceMember.Value;
    }

    public sealed class FileAttributesToIntConverter : IValueConverter<FileAttributesFlags, int>
    {
        public int Convert(FileAttributesFlags sourceMember, ResolutionContext context) => (int)sourceMember;
    }

    public sealed class IntToFileAttributesFlagsConverter : IValueConverter<int, FileAttributesFlags>
    {
        public FileAttributesFlags Convert(int sourceMember, ResolutionContext context) => (FileAttributesFlags)sourceMember;
    }

    public sealed class UtcTimestampToDateTimeOffsetConverter : IValueConverter<UtcTimestamp, DateTimeOffset>
    {
        public DateTimeOffset Convert(UtcTimestamp sourceMember, ResolutionContext context) => sourceMember.Value;
    }

    public sealed class NullableUtcTimestampToNullableDateTimeOffsetConverter : IValueConverter<UtcTimestamp?, DateTimeOffset?>
    {
        public DateTimeOffset? Convert(UtcTimestamp? sourceMember, ResolutionContext context) => sourceMember?.Value;
    }

    public sealed class DateTimeOffsetToUtcTimestampConverter : IValueConverter<DateTimeOffset, UtcTimestamp>
    {
        public UtcTimestamp Convert(DateTimeOffset sourceMember, ResolutionContext context) => UtcTimestamp.From(sourceMember);
    }

    public sealed class NullableDateTimeOffsetToNullableUtcTimestampConverter : IValueConverter<DateTimeOffset?, UtcTimestamp?>
    {
        public UtcTimestamp? Convert(DateTimeOffset? sourceMember, ResolutionContext context) =>
            sourceMember.HasValue ? UtcTimestamp.From(sourceMember.Value) : null;
    }
}
