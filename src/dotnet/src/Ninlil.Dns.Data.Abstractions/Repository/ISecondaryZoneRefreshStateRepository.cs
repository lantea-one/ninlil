using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the secondary
///     refresh service needs: the refresh state of one zone. The base <c>ListAsync</c> serves the
///     load at start (one row per secondary zone). Reads and writes stay on the primary database.
/// </summary>
public interface ISecondaryZoneRefreshStateRepository : IRepository<SecondaryZoneRefreshStateEntity>
{
    /// <summary>
    ///     This method finds the refresh state of a zone.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The row, or <see langword="null" /> if the zone has none.</returns>
    Task<SecondaryZoneRefreshStateEntity> FindByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    );
}
