using Microsoft.EntityFrameworkCore;

namespace Veriado.Appl.Abstractions;

/// <summary>
/// Provides validation for ensuring search projection writes execute inside the expected EF Core transaction scope.
/// </summary>
public interface ISearchProjectionTransactionGuard
{
    /// <summary>
    /// Ensures the supplied projection context is operating within the correct transactional scope.
    /// </summary>
    /// <param name="projectionContext">The projection DbContext instance.</param>
    void EnsureActiveTransaction(DbContext projectionContext);
}
