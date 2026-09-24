using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientGroupRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsClientGroupRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsClientGroupEntity>(context),
        IDnsClientGroupRepository
{
    /// <inheritdoc />
    public Task<DnsClientGroupEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(group => group.Name == name, stoppingToken);

    /// <inheritdoc />
    public Task<DnsClientGroupEntity> GetDefaultAsync(CancellationToken stoppingToken = default) =>
        Set.FirstOrDefaultAsync(group => group.IsDefault, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) => await Set.OrderBy(group => group.Name).Skip(skip).Take(take).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
