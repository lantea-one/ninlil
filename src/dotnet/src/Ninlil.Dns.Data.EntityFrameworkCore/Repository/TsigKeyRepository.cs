using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="ITsigKeyRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class TsigKeyRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<TsigKeyEntity>(context),
        ITsigKeyRepository
{
    /// <inheritdoc />
    public Task<TsigKeyEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(key => key.Name == name, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TsigKeyEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) => await Set.OrderBy(key => key.Name).Skip(skip).Take(take).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TsigKeyEntity>> ListUnusedClusterInviteKeysAsync(
        CancellationToken stoppingToken = default
    ) => await Set.Where(key => key.ClusterInviteExpiresAtUtc != null).ToListAsync(stoppingToken);
}
