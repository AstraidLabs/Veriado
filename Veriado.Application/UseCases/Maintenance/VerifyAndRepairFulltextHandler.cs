namespace Veriado.Appl.UseCases.Maintenance;

/// <summary>
/// Ensures that the search index accurately reflects the file corpus.
/// </summary>
public sealed class VerifyAndRepairFulltextHandler : IRequestHandler<VerifyAndRepairFulltextCommand, AppResult<int>>
{
    private readonly IFulltextIntegrityService _integrityService;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyAndRepairFulltextHandler"/> class.
    /// </summary>
    public VerifyAndRepairFulltextHandler(IFulltextIntegrityService integrityService)
    {
        _integrityService = integrityService;
    }

    /// <inheritdoc />
    public async Task<AppResult<int>> Handle(VerifyAndRepairFulltextCommand request, CancellationToken cancellationToken)
    {
        var report = await _integrityService.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var inconsistencies = report.MissingCount + report.OrphanCount;

        if (!request.Force && inconsistencies == 0)
        {
            return AppResult<int>.Success(0);
        }

        var repaired = await _integrityService
            .RepairAsync(request.Force, cancellationToken)
            .ConfigureAwait(false);

        return AppResult<int>.Success(repaired);
    }
}
