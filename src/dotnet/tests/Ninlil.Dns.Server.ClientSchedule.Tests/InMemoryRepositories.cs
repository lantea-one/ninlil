using System.Linq.Expressions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule.Tests;

public sealed class InMemoryDnsClientRepository : IDnsClientRepository
{
    public readonly List<DnsClientEntity> Items = [];

    /// <summary>
    ///     This field is this fake's own minimal stand-in for the real
    ///     <c>DnsClientAddressEntity</c> table <see cref="GetByClientIdentifierAsync" /> joins
    ///     through in production -- a test populates it (see each suite's own <c>AddClient</c>
    ///     helper) alongside adding the owning <see cref="DnsClientEntity" /> to <see cref="Items" />.
    /// </summary>
    public readonly List<(
        string ClientIdentifier,
        InternetProtocolVersionEnum ProtocolVersion,
        Guid ClientId
    )> Addresses = [];

    public Task<DnsClientEntity> GetByIdAsync(Guid id, CancellationToken stoppingToken = default) =>
        Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsClientEntity>> ListAsync(
        Expression<Func<DnsClientEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(DnsClientEntity entity, CancellationToken stoppingToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsClientEntity entity) { }

    public void Remove(DnsClientEntity entity) => Items.Remove(entity);

    public Task<DnsClientEntity> GetByClientIdentifierAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    )
    {
        Guid? clientId = Addresses
            .Where(address =>
                address.ClientIdentifier == clientIdentifier
                && address.ProtocolVersion == protocolVersion
            )
            .Select(address => (Guid?)address.ClientId)
            .FirstOrDefault();

        return Task.FromResult(
            clientId is null ? null : Items.FirstOrDefault(item => item.Id == clientId)
        );
    }

    // This fake holds no address rows, so only the client-level filters are honored here; the
    // presence filters and last-seen ordering are covered against the real repositories instead.
    public Task<IReadOnlyList<DnsClientEntity>> SearchAsync(
        DnsClientSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientEntity>>([
            .. Items
                .Where(item => Matches(item, criteria))
                .OrderBy(item => item.CreatedAtUtc)
                .Skip(skip)
                .Take(take),
        ]);

    public Task<int> CountAsync(
        DnsClientSearchCriteria criteria,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.Count(item => Matches(item, criteria)));

    private static bool Matches(DnsClientEntity item, DnsClientSearchCriteria criteria) =>
        (criteria?.IsFavorite is not { } favorite || item.IsFavorite == favorite)
        && (criteria?.CreatedSinceUtc is not { } since || item.CreatedAtUtc >= since);
}

public sealed class InMemoryDnsClientGroupRepository : IDnsClientGroupRepository
{
    public readonly List<DnsClientGroupEntity> Items = [];

    public Task<DnsClientGroupEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsClientGroupEntity>> ListAsync(
        Expression<Func<DnsClientGroupEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientGroupEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(DnsClientGroupEntity entity, CancellationToken stoppingToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsClientGroupEntity entity) { }

    public void Remove(DnsClientGroupEntity entity) => Items.Remove(entity);

    public Task<DnsClientGroupEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Name == name));

    public Task<DnsClientGroupEntity> GetDefaultAsync(CancellationToken stoppingToken = default) =>
        Task.FromResult(Items.FirstOrDefault(item => item.IsDefault));

    public Task<IReadOnlyList<DnsClientGroupEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientGroupEntity>>([
            .. Items.OrderBy(item => item.Name, StringComparer.Ordinal).Skip(skip).Take(take),
        ]);

    public Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        Task.FromResult(Items.Count);
}

public sealed class InMemoryDnsClientScheduleRepository : IDnsClientScheduleRepository
{
    public readonly List<DnsClientScheduleEntity> Items = [];

    public Task<DnsClientScheduleEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsClientScheduleEntity>> ListAsync(
        Expression<Func<DnsClientScheduleEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientScheduleEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(DnsClientScheduleEntity entity, CancellationToken stoppingToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsClientScheduleEntity entity) { }

    public void Remove(DnsClientScheduleEntity entity) => Items.Remove(entity);

    public Task<IReadOnlyList<DnsClientScheduleEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientScheduleEntity>>([
            .. Items.Where(item => item.ClientId == clientId),
        ]);

    public Task<IReadOnlyList<DnsClientScheduleEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientScheduleEntity>>([
            .. Items
                .Where(item => item.ClientId == clientId)
                .OrderBy(item => item.CreatedAtUtc)
                .Skip(skip)
                .Take(take),
        ]);

    public Task<int> CountByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.Count(item => item.ClientId == clientId));
}

public sealed class InMemoryDnsClientGroupScheduleRepository : IDnsClientGroupScheduleRepository
{
    public readonly List<DnsClientGroupScheduleEntity> Items = [];

    public Task<DnsClientGroupScheduleEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsClientGroupScheduleEntity>> ListAsync(
        Expression<Func<DnsClientGroupScheduleEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientGroupScheduleEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(
        DnsClientGroupScheduleEntity entity,
        CancellationToken stoppingToken = default
    )
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsClientGroupScheduleEntity entity) { }

    public void Remove(DnsClientGroupScheduleEntity entity) => Items.Remove(entity);

    public Task<IReadOnlyList<DnsClientGroupScheduleEntity>> ListByClientGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientGroupScheduleEntity>>([
            .. Items.Where(item => item.ClientGroupId == clientGroupId),
        ]);

    public Task<IReadOnlyList<DnsClientGroupScheduleEntity>> SearchByClientGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientGroupScheduleEntity>>([
            .. Items
                .Where(item => item.ClientGroupId == clientGroupId)
                .OrderBy(item => item.CreatedAtUtc)
                .Skip(skip)
                .Take(take),
        ]);

    public Task<int> CountByClientGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.Count(item => item.ClientGroupId == clientGroupId));
}
