using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutoMapper;
using Veriado.Contracts.Files;
using Veriado.Domain.Metadata;

namespace Veriado.Mapping.Profiles;

/// <summary>
/// Configures metadata conversions between domain types and DTOs.
/// </summary>
public sealed class MetadataProfiles : Profile
{
    private static readonly FieldInfo ValueField = typeof(MetadataValue).GetField("_value", BindingFlags.NonPublic | BindingFlags.Instance)!;

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
    }

    private static MetadataValueDto ConvertMetadataValueToDto(MetadataValue source, MetadataValueDto destination, ResolutionContext context)
    {
        var dto = new MetadataValueDto
        {
            Kind = source.Kind,
        };

        var raw = ValueField.GetValue(source);
        switch (source.Kind)
        {
            case MetadataValueKind.Null:
                break;
            case MetadataValueKind.String:
                dto.StringValue = raw as string;
                break;
            case MetadataValueKind.StringArray:
                dto.StringArrayValue = raw is string[] array ? array.ToArray() : Array.Empty<string>();
                break;
            case MetadataValueKind.UInt32:
                dto.UInt32Value = raw is uint u ? u : null;
                break;
            case MetadataValueKind.Int32:
                dto.Int32Value = raw is int i ? i : null;
                break;
            case MetadataValueKind.Double:
                dto.DoubleValue = raw is double d ? d : null;
                break;
            case MetadataValueKind.Boolean:
                dto.BooleanValue = raw is bool b ? b : null;
                break;
            case MetadataValueKind.Guid:
                dto.GuidValue = raw is Guid g ? g : null;
                break;
            case MetadataValueKind.FileTime:
                dto.FileTimeValue = raw is DateTimeOffset time ? time : null;
                break;
            case MetadataValueKind.Binary:
                dto.BinaryValue = raw is byte[] bytes ? bytes.ToArray() : null;
                break;
            default:
                throw new NotSupportedException($"Unsupported metadata value kind '{source.Kind}'.");
        }

        return dto;
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
}
