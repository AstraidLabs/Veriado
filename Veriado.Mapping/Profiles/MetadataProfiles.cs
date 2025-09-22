using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using AutoMapper;
using Veriado.Contracts.Files;
using Veriado.Domain.Metadata;

namespace Veriado.Mapping.Profiles;

/// <summary>
/// Configures metadata conversions between domain types and DTOs.
/// </summary>
public sealed class MetadataProfiles : Profile
{

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataProfiles"/> class.
    /// </summary>
    public MetadataProfiles()
    {
        CreateMap<MetadataValue, MetadataValueDto>().ConvertUsing(ConvertMetadataValueToDto);
        CreateMap<MetadataValueDto, MetadataValue>().ConvertUsing(ConvertMetadataValueFromDto);

        CreateMap<KeyValuePair<PropertyKey, MetadataValue>, ExtendedMetadataItemDto>()
            .ForMember(dest => dest.FormatId, opt => opt.MapFrom(src => src.Key.FormatId))
            .ForMember(dest => dest.PropertyId, opt => opt.MapFrom(src => src.Key.PropertyId))
            .ForMember(dest => dest.Remove, opt => opt.MapFrom(_ => false))
            .ForMember(dest => dest.Value, opt => opt.MapFrom(src => src.Value));

        CreateMap<ExtendedMetadata, IReadOnlyDictionary<string, string?>>()
            .ConvertUsing(ConvertExtendedMetadataToDictionary);
    }

    private static MetadataValueDto ConvertMetadataValueToDto(MetadataValue source, MetadataValueDto destination, ResolutionContext context)
    {
        var kind = ConvertKind(source.Kind);

        return kind switch
        {
            MetadataValueDtoKind.Null => new MetadataValueDto
            {
                Kind = kind,
            },
            MetadataValueDtoKind.String => new MetadataValueDto
            {
                Kind = kind,
                StringValue = source.TryGetString(out var value) ? value : null,
            },
            MetadataValueDtoKind.StringArray => new MetadataValueDto
            {
                Kind = kind,
                StringArrayValue = source.TryGetStringArray(out var values) && values is not null
                    ? values.ToArray()
                    : Array.Empty<string>(),
            },
            MetadataValueDtoKind.UInt32 => new MetadataValueDto
            {
                Kind = kind,
                UInt32Value = source.TryGetUInt32(out var number) ? number : null,
            },
            MetadataValueDtoKind.Int32 => new MetadataValueDto
            {
                Kind = kind,
                Int32Value = source.TryGetInt32(out var number) ? number : null,
            },
            MetadataValueDtoKind.Double => new MetadataValueDto
            {
                Kind = kind,
                DoubleValue = source.TryGetDouble(out var number) ? number : null,
            },
            MetadataValueDtoKind.Boolean => new MetadataValueDto
            {
                Kind = kind,
                BooleanValue = source.TryGetBoolean(out var boolean) ? boolean : null,
            },
            MetadataValueDtoKind.Guid => new MetadataValueDto
            {
                Kind = kind,
                GuidValue = source.TryGetGuid(out var guid) ? guid : null,
            },
            MetadataValueDtoKind.FileTime => new MetadataValueDto
            {
                Kind = kind,
                FileTimeValue = source.TryGetFileTime(out var timestamp) ? timestamp : null,
            },
            MetadataValueDtoKind.Binary => new MetadataValueDto
            {
                Kind = kind,
                BinaryValue = source.TryGetBinary(out var payload) && payload is not null
                    ? payload.ToArray()
                    : null,
            },
            _ => throw new NotSupportedException($"Unsupported metadata value kind '{source.Kind}'."),
        };
    }

    private static MetadataValueDtoKind ConvertKind(MetadataValueKind kind)
    {
        return kind switch
        {
            MetadataValueKind.Null => MetadataValueDtoKind.Null,
            MetadataValueKind.String => MetadataValueDtoKind.String,
            MetadataValueKind.StringArray => MetadataValueDtoKind.StringArray,
            MetadataValueKind.UInt32 => MetadataValueDtoKind.UInt32,
            MetadataValueKind.Int32 => MetadataValueDtoKind.Int32,
            MetadataValueKind.Double => MetadataValueDtoKind.Double,
            MetadataValueKind.Boolean => MetadataValueDtoKind.Boolean,
            MetadataValueKind.Guid => MetadataValueDtoKind.Guid,
            MetadataValueKind.FileTime => MetadataValueDtoKind.FileTime,
            MetadataValueKind.Binary => MetadataValueDtoKind.Binary,
            _ => throw new NotSupportedException($"Unsupported metadata value kind '{kind}'."),
        };
    }

    private static MetadataValue ConvertMetadataValueFromDto(MetadataValueDto source, MetadataValue destination, ResolutionContext context)
    {
        return source.Kind switch
        {
            MetadataValueDtoKind.Null => MetadataValue.Null,
            MetadataValueDtoKind.String => MetadataValue.FromString(source.StringValue ?? throw new ArgumentException("String value is required.")),
            MetadataValueDtoKind.StringArray => MetadataValue.FromStringArray(source.StringArrayValue ?? Array.Empty<string>()),
            MetadataValueDtoKind.UInt32 => MetadataValue.FromUInt(source.UInt32Value ?? throw new ArgumentException("UInt32 value is required.")),
            MetadataValueDtoKind.Int32 => MetadataValue.FromInt(source.Int32Value ?? throw new ArgumentException("Int32 value is required.")),
            MetadataValueDtoKind.Double => MetadataValue.FromReal(source.DoubleValue ?? throw new ArgumentException("Double value is required.")),
            MetadataValueDtoKind.Boolean => MetadataValue.FromBool(source.BooleanValue ?? throw new ArgumentException("Boolean value is required.")),
            MetadataValueDtoKind.Guid => MetadataValue.FromGuid(source.GuidValue ?? throw new ArgumentException("Guid value is required.")),
            MetadataValueDtoKind.FileTime => MetadataValue.FromFileTime(source.FileTimeValue ?? throw new ArgumentException("Timestamp value is required.")),
            MetadataValueDtoKind.Binary => MetadataValue.FromBinary(source.BinaryValue ?? Array.Empty<byte>()),
            _ => throw new NotSupportedException($"Unsupported metadata value kind '{source.Kind}'."),
        };
    }

    private static IReadOnlyDictionary<string, string?> ConvertExtendedMetadataToDictionary(
        ExtendedMetadata source,
        IReadOnlyDictionary<string, string?> destination,
        ResolutionContext context)
    {
        if (source is null)
        {
            return new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(StringComparer.Ordinal));
        }

        var materialized = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in source.AsEnumerable())
        {
            materialized[pair.Key.ToString()] = FormatMetadataValue(pair.Value);
        }

        return new ReadOnlyDictionary<string, string?>(materialized);
    }

    private static string? FormatMetadataValue(MetadataValue value)
    {
        if (value.TryGetString(out var single))
        {
            return single;
        }

        if (value.TryGetStringArray(out var array) && array is { Length: > 0 })
        {
            return string.Join(", ", array);
        }

        if (value.TryGetGuid(out var guid))
        {
            return guid.ToString("D", CultureInfo.InvariantCulture);
        }

        if (value.TryGetFileTime(out var fileTime))
        {
            return fileTime.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value.TryGetBinary(out var binary) && binary is { Length: > 0 })
        {
            return Convert.ToBase64String(binary);
        }

        if (value.TryGetBoolean(out var boolean))
        {
            return boolean.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetInt32(out var intValue))
        {
            return intValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetUInt32(out var uintValue))
        {
            return uintValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetDouble(out var doubleValue))
        {
            return doubleValue.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }
}
