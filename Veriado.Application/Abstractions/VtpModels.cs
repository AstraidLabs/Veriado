using System;
using Veriado.Contracts.Storage;
using ContractsVtpImportItemStatus = Veriado.Contracts.Storage.VtpImportItemStatus;
using ContractsVtpImportResultCode = Veriado.Contracts.Storage.VtpImportResultCode;
using ContractsVtpPackageInfo = Veriado.Contracts.Storage.VtpPackageInfo;
using ContractsVtpPayloadType = Veriado.Contracts.Storage.VtpPayloadType;

namespace Veriado.Appl.Abstractions;

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

public sealed record VtpPackageInfo(
    string Protocol,
    string ProtocolVersion,
    VtpPayloadType PayloadType,
    Guid PackageId,
    Guid CorrelationId,
    Guid SourceInstanceId,
    string? SourceInstanceName,
    Guid TargetInstanceId,
    string? TargetInstanceName)
{
    public static VtpPackageInfo Default(Guid? packageId = null, Guid? correlationId = null, Guid? sourceInstanceId = null, Guid? targetInstanceId = null)
        => new(
            "Veriado.Transfer",
            "1.0",
            VtpPayloadType.VpfPackage,
            packageId ?? Guid.NewGuid(),
            correlationId ?? Guid.NewGuid(),
            sourceInstanceId ?? Guid.Empty,
            null,
            targetInstanceId ?? Guid.Empty,
            null);
}

public static class VtpMappings
{
    public static VtpPackageInfo ToModel(this ContractsVtpPackageInfo dto)
        => new(
            dto.Protocol,
            dto.ProtocolVersion,
            (VtpPayloadType)dto.PayloadType,
            dto.PackageId,
            dto.CorrelationId,
            dto.SourceInstanceId,
            dto.SourceInstanceName,
            dto.TargetInstanceId,
            dto.TargetInstanceName);

    public static ContractsVtpPackageInfo ToContract(this VtpPackageInfo model)
        => new()
        {
            Protocol = model.Protocol,
            ProtocolVersion = model.ProtocolVersion,
            PayloadType = (ContractsVtpPayloadType)model.PayloadType,
            PackageId = model.PackageId,
            CorrelationId = model.CorrelationId,
            SourceInstanceId = model.SourceInstanceId,
            SourceInstanceName = model.SourceInstanceName,
            TargetInstanceId = model.TargetInstanceId,
            TargetInstanceName = model.TargetInstanceName,
        };

    public static VtpImportItemStatus ToModel(this ContractsVtpImportItemStatus status)
        => (VtpImportItemStatus)status;

    public static ContractsVtpImportItemStatus ToContract(this VtpImportItemStatus status)
        => (ContractsVtpImportItemStatus)status;

    public static VtpImportResultCode ToModel(this ContractsVtpImportResultCode status)
        => (VtpImportResultCode)status;

    public static ContractsVtpImportResultCode ToContract(this VtpImportResultCode status)
        => (ContractsVtpImportResultCode)status;
}
