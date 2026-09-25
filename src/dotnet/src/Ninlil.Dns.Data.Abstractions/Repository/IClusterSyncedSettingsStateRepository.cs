using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup cluster config
///     sync needs for conflict resolution: the row, if any, recorded for a settings section.
/// </summary>
public interface IClusterSyncedSettingsStateRepository
    : IRepository<ClusterSyncedSettingsStateEntity>
{
    /// <summary>
    ///     This method looks up the recorded sync state for one settings section by its
    ///     <see cref="ClusterSyncedSettingsStateEntity.SectionName" />.
    /// </summary>
    /// <param name="sectionName">The section name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching row, or <see langword="null" /> if this section has never been recorded.</returns>
    Task<ClusterSyncedSettingsStateEntity> FindBySectionNameAsync(
        string sectionName,
        CancellationToken stoppingToken = default
    );
}
