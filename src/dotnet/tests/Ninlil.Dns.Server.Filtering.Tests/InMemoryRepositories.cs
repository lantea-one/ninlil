using System.Linq.Expressions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

public sealed class InMemoryDnsFilterListRepository : IDnsFilterListRepository
{
    public readonly List<DnsFilterListEntity> Items = [];

    public Task<DnsFilterListEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsFilterListEntity>> ListAsync(
        Expression<Func<DnsFilterListEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsFilterListEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(DnsFilterListEntity entity, CancellationToken stoppingToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsFilterListEntity entity) { }

    public void Remove(DnsFilterListEntity entity) => Items.Remove(entity);

    public Task<DnsFilterListEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Name == name));

    public Task<IReadOnlyList<DnsFilterListEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsFilterListEntity>>([
            .. Items.OrderBy(item => item.Name, StringComparer.Ordinal).Skip(skip).Take(take),
        ]);

    public Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        Task.FromResult(Items.Count);
}

public sealed class InMemoryDnsFilterListEntryRepository : IDnsFilterListEntryRepository
{
    public readonly List<DnsFilterListEntryEntity> Items = [];

    public Task<DnsFilterListEntryEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsFilterListEntryEntity>> ListAsync(
        Expression<Func<DnsFilterListEntryEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsFilterListEntryEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(DnsFilterListEntryEntity entity, CancellationToken stoppingToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsFilterListEntryEntity entity) { }

    public void Remove(DnsFilterListEntryEntity entity) => Items.Remove(entity);

    public Task<IReadOnlyList<DnsFilterListEntryEntity>> ListByListIdAsync(
        Guid listId,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsFilterListEntryEntity>>([
            .. Items.Where(item => item.ListId == listId),
        ]);

    public Task<IReadOnlyList<DnsFilterListEntryEntity>> SearchByListIdAsync(
        Guid listId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsFilterListEntryEntity>>([
            .. Items
                .Where(item => item.ListId == listId)
                .OrderBy(item => item.Value, StringComparer.Ordinal)
                .Skip(skip)
                .Take(take),
        ]);

    public Task<int> CountByListIdAsync(Guid listId, CancellationToken stoppingToken = default) =>
        Task.FromResult(Items.Count(item => item.ListId == listId));

    public Task ReplaceEntriesAsync(
        Guid listId,
        IReadOnlyList<DnsFilterListReplacementEntry> entries,
        CancellationToken stoppingToken = default
    )
    {
        Items.RemoveAll(item => item.ListId == listId);
        Items.AddRange(
            entries.Select(entry => new DnsFilterListEntryEntity
            {
                Id = Guid.NewGuid(),
                ListId = listId,
                Value = entry.Value,
                IsException = entry.IsException,
            })
        );
        return Task.CompletedTask;
    }
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

public sealed class InMemoryDnsClientGroupNetworkRepository : IDnsClientGroupNetworkRepository
{
    public readonly List<DnsClientGroupNetworkEntity> Items = [];

    public Task<DnsClientGroupNetworkEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsClientGroupNetworkEntity>> ListAsync(
        Expression<Func<DnsClientGroupNetworkEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientGroupNetworkEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(
        DnsClientGroupNetworkEntity entity,
        CancellationToken stoppingToken = default
    )
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsClientGroupNetworkEntity entity) { }

    public void Remove(DnsClientGroupNetworkEntity entity) => Items.Remove(entity);

    public Task<IReadOnlyList<DnsClientGroupNetworkEntity>> ListByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientGroupNetworkEntity>>([
            .. Items.Where(item => item.ClientGroupId == clientGroupId),
        ]);

    public Task<IReadOnlyList<DnsClientGroupNetworkEntity>> SearchByGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientGroupNetworkEntity>>([
            .. Items
                .Where(item => item.ClientGroupId == clientGroupId)
                .OrderBy(item => item.Cidr, StringComparer.Ordinal)
                .Skip(skip)
                .Take(take),
        ]);

    public Task<int> CountByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.Count(item => item.ClientGroupId == clientGroupId));
}

public sealed class InMemoryDnsClientGroupListAssignmentRepository
    : IDnsClientGroupListAssignmentRepository
{
    public readonly List<DnsClientGroupListAssignmentEntity> Items = [];

    public Task<DnsClientGroupListAssignmentEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsClientGroupListAssignmentEntity>> ListAsync(
        Expression<Func<DnsClientGroupListAssignmentEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientGroupListAssignmentEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(
        DnsClientGroupListAssignmentEntity entity,
        CancellationToken stoppingToken = default
    )
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsClientGroupListAssignmentEntity entity) { }

    public void Remove(DnsClientGroupListAssignmentEntity entity) => Items.Remove(entity);

    public Task<IReadOnlyList<DnsClientGroupListAssignmentEntity>> ListByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientGroupListAssignmentEntity>>([
            .. Items.Where(item => item.ClientGroupId == clientGroupId),
        ]);

    public Task<IReadOnlyList<DnsClientGroupListAssignmentEntity>> SearchByGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientGroupListAssignmentEntity>>([
            .. Items
                .Where(item => item.ClientGroupId == clientGroupId)
                .OrderBy(item => item.CreatedAtUtc)
                .Skip(skip)
                .Take(take),
        ]);

    public Task<int> CountByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.Count(item => item.ClientGroupId == clientGroupId));
}

public sealed class InMemoryDnsClientRepository : IDnsClientRepository
{
    public readonly List<DnsClientEntity> Items = [];

    /// <summary>
    ///     This field is this fake's own minimal stand-in for the real
    ///     <c>DnsClientAddressEntity</c> table <see cref="GetByClientIdentifierAsync" /> joins
    ///     through in production -- a test populates it (see <c>Fixture.AddClient</c>) alongside
    ///     adding the owning <see cref="DnsClientEntity" /> to <see cref="Items" />.
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

public sealed class InMemoryDnsClientListAssignmentRepository : IDnsClientListAssignmentRepository
{
    public readonly List<DnsClientListAssignmentEntity> Items = [];

    public Task<DnsClientListAssignmentEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<DnsClientListAssignmentEntity>> ListAsync(
        Expression<Func<DnsClientListAssignmentEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientListAssignmentEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(
        DnsClientListAssignmentEntity entity,
        CancellationToken stoppingToken = default
    )
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(DnsClientListAssignmentEntity entity) { }

    public void Remove(DnsClientListAssignmentEntity entity) => Items.Remove(entity);

    public Task<IReadOnlyList<DnsClientListAssignmentEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientListAssignmentEntity>>([
            .. Items.Where(item => item.ClientId == clientId),
        ]);

    public Task<IReadOnlyList<DnsClientListAssignmentEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsClientListAssignmentEntity>>([
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

public sealed class NoOpUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default) =>
        Task.FromResult(0);
}
