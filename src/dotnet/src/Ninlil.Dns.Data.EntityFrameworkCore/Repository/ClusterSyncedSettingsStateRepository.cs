using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IClusterSyncedSettingsStateRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class ClusterSyncedSettingsStateRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<ClusterSyncedSettingsStateEntity>(context),
        IClusterSyncedSettingsStateRepository
{
    /// <inheritdoc />
    public Task<ClusterSyncedSettingsStateEntity> FindBySectionNameAsync(
        string sectionName,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(state => state.SectionName == sectionName, stoppingToken);
}
