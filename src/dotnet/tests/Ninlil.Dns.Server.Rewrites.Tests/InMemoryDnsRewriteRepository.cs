using System.Linq.Expressions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.Tests;

/// <summary>
///     This class is an in-memory <see cref="IDnsRewriteRepository" /> that
///     <see cref="DnsRewriteEngineTests" /> fills directly through <see cref="Items" />.
/// </summary>
public sealed class InMemoryDnsRewriteRepository : IDnsRewriteRepository
{
    public readonly List<DnsRewriteEntity> Items = [];

    public Task<DnsRewriteEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsRewriteEntity>> ListAsync(
        Expression<Func<DnsRewriteEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsRewriteEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(DnsRewriteEntity entity, CancellationToken stoppingToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsRewriteEntity entity) { }

    public void Remove(DnsRewriteEntity entity) => Items.Remove(entity);

    public Task<IReadOnlyList<DnsRewriteEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsRewriteEntity>>([
            .. Items.Where(item => !item.IsDisabled),
        ]);

    public Task<IReadOnlyList<DnsRewriteEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsRewriteEntity>>([
            .. Items
                .OrderBy(item => item.Domain, StringComparer.Ordinal)
                .ThenBy(item => item.CreatedAtUtc)
                .Skip(skip)
                .Take(take),
        ]);

    public Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        Task.FromResult(Items.Count);
}
