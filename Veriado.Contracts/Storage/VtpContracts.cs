using System;
using System.Text.Json.Serialization;

namespace Veriado.Contracts.Storage;

public enum VtpPayloadType
{
    Unknown = 0,
    VpfPackage = 1,
}

public enum VtpImportItemStatus
{
    Unknown = 0,
    Imported = 1,
    Skipped = 2,
    Conflicted = 3,
}

public enum VtpImportResultCode
{
    Unknown = 0,
    Success = 1,
    PartialSuccess = 2,
    Failed = 3,
}

public sealed record VtpPackageInfo
{
    [JsonPropertyName("protocol")]
    public string Protocol { get; init; } = "VTP";

    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; init; } = "1.0";

    [JsonPropertyName("payloadType")]
    public VtpPayloadType PayloadType { get; init; } = VtpPayloadType.VpfPackage;

    [JsonPropertyName("packageId")]
    public Guid PackageId { get; init; } = Guid.NewGuid();

    [JsonPropertyName("correlationId")]
    public Guid CorrelationId { get; init; } = Guid.NewGuid();

    [JsonPropertyName("sourceInstanceId")]
    public Guid SourceInstanceId { get; init; } = Guid.Empty;

    [JsonPropertyName("targetInstanceId")]
    public Guid TargetInstanceId { get; init; } = Guid.Empty;
}
