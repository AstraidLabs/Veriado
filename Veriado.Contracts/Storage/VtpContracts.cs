using System;
using System.Text.Json.Serialization;

namespace Veriado.Contracts.Storage;

public enum VtpPayloadType
{
    Unknown = 0,
    VpfPackage = 1,
    FullExport = 2,
    DeltaExport = 3,
    Backup = 4,
}

public enum VtpImportItemStatus
{
    Unknown = 0,
    New = 1,
    Same = 2,
    Updated = 3,
    SkippedOlder = 4,
    Conflict = 5,
    Failed = 6,
}

public enum VtpImportResultCode
{
    Unknown = -1,
    Ok = 0,
    OkWithWarnings = 1,
    PartialSuccess = 2,
    Failed = 3,
}

public sealed record VtpPackageInfo
{
    [JsonPropertyName("protocol")]
    public string Protocol { get; init; } = "Veriado.Transfer";

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

    [JsonPropertyName("sourceInstanceName")]
    public string? SourceInstanceName { get; init; }
        = null;

    [JsonPropertyName("targetInstanceId")]
    public Guid TargetInstanceId { get; init; } = Guid.Empty;

    [JsonPropertyName("targetInstanceName")]
    public string? TargetInstanceName { get; init; }
        = null;
}
