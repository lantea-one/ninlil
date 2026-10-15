using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="ISecondaryZoneRefreshStateRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class SecondaryZoneRefreshStateRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<SecondaryZoneRefreshStateEntity>(context),
        ISecondaryZoneRefreshStateRepository
{
    /// <inheritdoc />
    public async Task<SecondaryZoneRefreshStateEntity> FindByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    ) => await Set.FirstOrDefaultAsync(state => state.ZoneId == zoneId, stoppingToken);
}
