using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IZoneRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class ZoneRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<ZoneEntity>(context),
        IZoneRepository
{
    /// <inheritdoc />
    public Task<ZoneEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(zone => zone.Name == name, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ZoneEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) => await Set.OrderBy(zone => zone.Name).Skip(skip).Take(take).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
