using System;

namespace Veriado.Contracts.Storage;

public sealed record ExportRequestDto
{
    public string DestinationPath { get; init; } = string.Empty;
    public string? PackageName { get; init; }
        = null;
    public string? Description { get; init; }
        = null;
    public bool OverwriteExisting { get; init; }
        = false;
    public bool EncryptPayload { get; init; }
        = false;
    public string? Password { get; init; }
        = null;
    public bool SignPayload { get; init; }
        = false;
    public Guid? SourceInstanceId { get; init; }
        = null;
    public string? SourceInstanceName { get; init; }
        = null;
}

public sealed record ExportResultDto
{
    public StorageOperationResultDto Operation { get; init; } = new();
    public string? PackageId { get; init; }
        = null;
    public string? FinalPath { get; init; }
        = null;
}
