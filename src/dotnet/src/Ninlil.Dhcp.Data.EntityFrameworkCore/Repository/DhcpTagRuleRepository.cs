using Microsoft.EntityFrameworkCore;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDhcpTagRuleRepository" /> against the shared
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DhcpTagRuleRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DhcpTagRuleEntity>(context),
        IDhcpTagRuleRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpTagRuleEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) => await Set.OrderBy(rule => rule.Name).Skip(skip).Take(take).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpTagRuleEntity>> ListEnabledForScopeAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(rule =>
                !rule.IsDisabled && (rule.ScopeId == scopeId || rule.ScopeId == null)
            )
            .ToListAsync(stoppingToken);
}
