using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientGroupNetworkRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsClientGroupNetworkRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsClientGroupNetworkEntity>(context),
        IDnsClientGroupNetworkRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupNetworkEntity>> ListByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(network => network.ClientGroupId == clientGroupId)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupNetworkEntity>> SearchByGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.AsNoTracking()
            .Where(network => network.ClientGroupId == clientGroupId)
            .OrderBy(network => network.Cidr)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(network => network.ClientGroupId == clientGroupId, stoppingToken);
}
