using Veriado.Contracts.Files;

namespace Veriado.WinUI.Services.Abstractions;

public interface IHotStateService
{
    string? LastQuery { get; set; }

    string? LastFolder { get; set; }

    int PageSize { get; set; }

    bool ImportRecursive { get; set; }

    bool ImportKeepFsMetadata { get; set; }

    bool ImportSetReadOnly { get; set; }

    bool ImportUseParallel { get; set; }

    int ImportMaxDegreeOfParallelism { get; set; }

    string? ImportDefaultAuthor { get; set; }

    double? ImportMaxFileSizeMegabytes { get; set; }

    bool ImportAutoExportLog { get; set; }

    ValidityThresholds ValidityThresholds { get; }

    int ValidityRedThresholdDays { get; set; }

    int ValidityOrangeThresholdDays { get; set; }

    int ValidityGreenThresholdDays { get; set; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
