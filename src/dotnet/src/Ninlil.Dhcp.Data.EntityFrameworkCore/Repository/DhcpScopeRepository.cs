using Microsoft.EntityFrameworkCore;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDhcpScopeRepository" /> against the shared
///     <see cref="NinlilDnsContext" />. DHCP entities are mapped in the same model as the DNS
///     entities, not in a separate database.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DhcpScopeRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DhcpScopeEntity>(context),
        IDhcpScopeRepository
{
    /// <inheritdoc />
    public Task<DhcpScopeEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(scope => scope.Name == name, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpScopeEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.AsNoTracking()
            .OrderBy(scope => scope.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
