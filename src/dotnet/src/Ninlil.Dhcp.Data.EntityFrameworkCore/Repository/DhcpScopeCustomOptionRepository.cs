using Microsoft.EntityFrameworkCore;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDhcpScopeCustomOptionRepository" /> against the shared
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DhcpScopeCustomOptionRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DhcpScopeCustomOptionEntity>(context),
        IDhcpScopeCustomOptionRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpScopeCustomOptionEntity>> SearchByScopeIdAsync(
        Guid scopeId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.AsNoTracking()
            .Where(customOption => customOption.ScopeId == scopeId)
            .OrderBy(customOption => customOption.OptionTag)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByScopeIdAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(customOption => customOption.ScopeId == scopeId, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpScopeCustomOptionEntity>> ListEnabledByScopeIdAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    ) =>
        await Set.AsNoTracking()
            .Where(customOption => customOption.ScopeId == scopeId && !customOption.IsDisabled)
            .ToListAsync(stoppingToken);
}
