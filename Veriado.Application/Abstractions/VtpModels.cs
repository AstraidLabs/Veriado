using System;
using Contracts = Veriado.Contracts.Storage;

namespace Veriado.Application.Abstractions;

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

public sealed record VtpPackageInfo(
    string Protocol,
    string ProtocolVersion,
    VtpPayloadType PayloadType,
    Guid PackageId,
    Guid CorrelationId,
    Guid SourceInstanceId,
    Guid TargetInstanceId)
{
    public static VtpPackageInfo Default(Guid? packageId = null, Guid? correlationId = null, Guid? sourceInstanceId = null, Guid? targetInstanceId = null)
        => new(
            "VTP",
            "1.0",
            VtpPayloadType.VpfPackage,
            packageId ?? Guid.NewGuid(),
            correlationId ?? Guid.NewGuid(),
            sourceInstanceId ?? Guid.Empty,
            targetInstanceId ?? Guid.Empty);
}

public static class VtpMappings
{
    public static VtpPackageInfo ToModel(this Contracts.VtpPackageInfo dto)
        => new(
            dto.Protocol,
            dto.ProtocolVersion,
            (VtpPayloadType)dto.PayloadType,
            dto.PackageId,
            dto.CorrelationId,
            dto.SourceInstanceId,
            dto.TargetInstanceId);

    public static Contracts.VtpPackageInfo ToContract(this VtpPackageInfo model)
        => new()
        {
            Protocol = model.Protocol,
            ProtocolVersion = model.ProtocolVersion,
            PayloadType = (Contracts.VtpPayloadType)model.PayloadType,
            PackageId = model.PackageId,
            CorrelationId = model.CorrelationId,
            SourceInstanceId = model.SourceInstanceId,
            TargetInstanceId = model.TargetInstanceId,
        };

    public static VtpImportItemStatus ToModel(this Contracts.VtpImportItemStatus status)
        => (VtpImportItemStatus)status;

    public static Contracts.VtpImportItemStatus ToContract(this VtpImportItemStatus status)
        => (Contracts.VtpImportItemStatus)status;

    public static VtpImportResultCode ToModel(this Contracts.VtpImportResultCode status)
        => (VtpImportResultCode)status;

    public static Contracts.VtpImportResultCode ToContract(this VtpImportResultCode status)
        => (Contracts.VtpImportResultCode)status;
}
