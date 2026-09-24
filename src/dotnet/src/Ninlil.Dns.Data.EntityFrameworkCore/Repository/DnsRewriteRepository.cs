using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsRewriteRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsRewriteRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsRewriteEntity>(context),
        IDnsRewriteRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsRewriteEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    ) => await Set.Where(rewrite => !rewrite.IsDisabled).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsRewriteEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.OrderBy(rewrite => rewrite.Domain)
            .ThenBy(rewrite => rewrite.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
