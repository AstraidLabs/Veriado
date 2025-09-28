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

        profile.CreateMap<FileName, string>().ConvertUsing(static (FileName source, ResolutionContext _) =>
            source.Value ?? string.Empty);
        profile.CreateMap<FileName?, string?>().ConvertUsing(static (FileName? source, ResolutionContext _) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<FileExtension, string>().ConvertUsing(static (FileExtension source, ResolutionContext _) =>
            source.Value ?? string.Empty);
        profile.CreateMap<FileExtension?, string?>().ConvertUsing(static (FileExtension? source, ResolutionContext _) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<MimeType, string>().ConvertUsing(static (MimeType source, ResolutionContext _) =>
            source.Value ?? string.Empty);
        profile.CreateMap<MimeType?, string?>().ConvertUsing(static (MimeType? source, ResolutionContext _) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<FileHash, string>().ConvertUsing(static (FileHash source, ResolutionContext _) =>
            source.Value ?? string.Empty);
        profile.CreateMap<FileHash?, string?>().ConvertUsing(static (FileHash? source, ResolutionContext _) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<ByteSize, long>().ConvertUsing(static (ByteSize source, ResolutionContext _) => source.Value);
        profile.CreateMap<ByteSize?, long?>().ConvertUsing(static (ByteSize? source, ResolutionContext _) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<FileAttributesFlags, int>().ConvertUsing(static (FileAttributesFlags source, ResolutionContext _) =>
            (int)source);
        profile.CreateMap<int, FileAttributesFlags>().ConvertUsing(static (int source, ResolutionContext _) =>
            (FileAttributesFlags)source);

        profile.CreateMap<UtcTimestamp, DateTimeOffset>().ConvertUsing(static (UtcTimestamp source, ResolutionContext _) =>
            source.Value);
        profile.CreateMap<UtcTimestamp, DateTimeOffset?>().ConvertUsing(static (UtcTimestamp source, ResolutionContext _) =>
            source.Value);
        profile.CreateMap<UtcTimestamp?, DateTimeOffset?>().ConvertUsing(static (UtcTimestamp? source, ResolutionContext _) =>
            source.HasValue ? source.Value.Value : null);

        profile.CreateMap<DateTimeOffset, UtcTimestamp>().ConvertUsing(static (DateTimeOffset source, ResolutionContext _) =>
            UtcTimestamp.From(source));
        profile.CreateMap<DateTimeOffset?, UtcTimestamp?>().ConvertUsing(static (DateTimeOffset? source, ResolutionContext _) =>
            source.HasValue ? UtcTimestamp.From(source.Value) : null);
    }
}
