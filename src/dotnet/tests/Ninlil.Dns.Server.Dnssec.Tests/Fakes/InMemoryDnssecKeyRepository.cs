using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Tests.Fakes;

/// <summary>
///     This class is an in-memory <see cref="IDnssecKeyRepository" /> for tests that exercise
///     <see cref="DnssecKeyManagementService" /> without a database. <see cref="Items" /> exposes
///     the stored keys so a test can seed or inspect them.
/// </summary>
public sealed class InMemoryDnssecKeyRepository : IDnssecKeyRepository
{
    /// <summary>
    ///     This field contains the stored keys.
    /// </summary>
    public readonly List<DnssecKeyEntity> Items = [];

    /// <inheritdoc />
    public Task<DnssecKeyEntity> GetByIdAsync(Guid id, CancellationToken stoppingToken = default) =>
        Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    /// <inheritdoc />
    public Task<IReadOnlyList<DnssecKeyEntity>> ListAsync(
        System.Linq.Expressions.Expression<Func<DnssecKeyEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnssecKeyEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    /// <inheritdoc />
    public Task AddAsync(DnssecKeyEntity entity, CancellationToken stoppingToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Update(DnssecKeyEntity entity) { }

    /// <inheritdoc />
    public void Remove(DnssecKeyEntity entity) => Items.Remove(entity);

    /// <inheritdoc />
    public Task<IReadOnlyList<DnssecKeyEntity>> ListByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnssecKeyEntity>>([
            .. Items.Where(item => item.ZoneId == zoneId),
        ]);

    /// <inheritdoc />
    public Task<IReadOnlyList<DnssecKeyEntity>> SearchByZoneIdAsync(
        Guid zoneId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnssecKeyEntity>>([
            .. Items
                .Where(item => item.ZoneId == zoneId)
                .OrderBy(item => item.Role)
                .ThenBy(item => item.CreatedAtUtc)
                .Skip(skip)
                .Take(take),
        ]);

    /// <inheritdoc />
    public Task<int> CountByZoneIdAsync(Guid zoneId, CancellationToken stoppingToken = default) =>
        Task.FromResult(Items.Count(item => item.ZoneId == zoneId));
}
