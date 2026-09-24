using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the one lookup the
///     cluster's own config-sync mechanism needs for a settings section's own conflict-resolution
///     bookkeeping: find the single row (if any) already recorded for a given section name.
/// </summary>
public interface IClusterSyncedSettingsStateRepository
    : IRepository<ClusterSyncedSettingsStateEntity>
{
    /// <summary>
    ///     This method looks up the recorded sync state for a single settings section by its own
    ///     stable <see cref="ClusterSyncedSettingsStateEntity.SectionName" />.
    /// </summary>
    /// <param name="sectionName">The section name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching row, or <see langword="null" /> if this section has never been recorded.</returns>
    Task<ClusterSyncedSettingsStateEntity> FindBySectionNameAsync(
        string sectionName,
        CancellationToken stoppingToken = default
    );
}
