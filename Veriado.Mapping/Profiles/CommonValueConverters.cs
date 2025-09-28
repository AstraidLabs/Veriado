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

        profile.CreateMap<FileName, string>().ConvertUsing(static (FileName source) =>
            source.Value ?? string.Empty);
        profile.CreateMap<FileName?, string?>().ConvertUsing(static (FileName? source) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<FileExtension, string>().ConvertUsing(static (FileExtension source) =>
            source.Value ?? string.Empty);
        profile.CreateMap<FileExtension?, string?>().ConvertUsing(static (FileExtension? source) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<MimeType, string>().ConvertUsing(static (MimeType source) =>
            source.Value ?? string.Empty);
        profile.CreateMap<MimeType?, string?>().ConvertUsing(static (MimeType? source) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<FileHash, string>().ConvertUsing(static (FileHash source) =>
            source.Value ?? string.Empty);
        profile.CreateMap<FileHash?, string?>().ConvertUsing(static (FileHash? source) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<ByteSize, long>().ConvertUsing(static (ByteSize source) => source.Value);
        profile.CreateMap<ByteSize?, long?>().ConvertUsing(static (ByteSize? source) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<FileAttributesFlags, int>().ConvertUsing(static (FileAttributesFlags source) =>
            (int)source);
        profile.CreateMap<int, FileAttributesFlags>().ConvertUsing(static (int source) =>
            (FileAttributesFlags)source);

        profile.CreateMap<UtcTimestamp, DateTimeOffset>().ConvertUsing(static (UtcTimestamp source) =>
            source.Value);
        profile.CreateMap<UtcTimestamp, DateTimeOffset?>().ConvertUsing(static (UtcTimestamp source) =>
            source.Value);
        profile.CreateMap<UtcTimestamp?, DateTimeOffset?>().ConvertUsing(static (UtcTimestamp? source) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<DateTimeOffset, UtcTimestamp>().ConvertUsing(static (DateTimeOffset source) =>
            UtcTimestamp.From(source));
        profile.CreateMap<DateTimeOffset?, UtcTimestamp?>().ConvertUsing(static (DateTimeOffset? source) =>
            source.HasValue ? UtcTimestamp.From(source.Value) : null);
    }
}
