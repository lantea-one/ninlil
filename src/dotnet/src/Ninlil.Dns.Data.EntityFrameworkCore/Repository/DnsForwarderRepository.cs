using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsForwarderRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsForwarderRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsForwarderEntity>(context),
        IDnsForwarderRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsForwarderEntity>> ListEnabledOrderedAsync(
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(forwarder => !forwarder.IsDisabled)
            .OrderBy(forwarder => forwarder.SortOrder)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsForwarderEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.OrderBy(forwarder => forwarder.SortOrder)
            .ThenBy(forwarder => forwarder.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
