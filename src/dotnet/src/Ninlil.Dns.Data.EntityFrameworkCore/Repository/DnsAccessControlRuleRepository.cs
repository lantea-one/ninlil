using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsAccessControlRuleRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsAccessControlRuleRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsAccessControlRuleEntity>(context),
        IDnsAccessControlRuleRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsAccessControlRuleEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    ) => await Set.AsNoTracking().Where(rule => !rule.IsDisabled).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsAccessControlRuleEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.AsNoTracking()
            .OrderBy(rule => rule.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
