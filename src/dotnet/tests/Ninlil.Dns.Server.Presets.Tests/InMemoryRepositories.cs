using System.Linq.Expressions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets.Tests;

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

public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(0);
    }
}
