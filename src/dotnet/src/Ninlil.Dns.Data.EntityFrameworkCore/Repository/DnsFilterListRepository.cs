using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsFilterListRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsFilterListRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsFilterListEntity>(context),
        IDnsFilterListRepository
{
    /// <inheritdoc />
    public Task<DnsFilterListEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(list => list.Name == name, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsFilterListEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) => await Set.OrderBy(list => list.Name).Skip(skip).Take(take).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
