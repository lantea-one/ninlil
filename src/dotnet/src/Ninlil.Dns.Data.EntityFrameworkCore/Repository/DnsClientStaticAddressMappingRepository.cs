using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientStaticAddressMappingRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsClientStaticAddressMappingRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsClientStaticAddressMappingEntity>(context),
        IDnsClientStaticAddressMappingRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientStaticAddressMappingEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    ) => await Set.Where(mapping => !mapping.IsDisabled).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientStaticAddressMappingEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.OrderBy(mapping => mapping.Cidr).Skip(skip).Take(take).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
