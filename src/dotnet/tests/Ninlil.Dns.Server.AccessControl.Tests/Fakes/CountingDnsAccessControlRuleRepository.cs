using System.Linq.Expressions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl.Tests.Fakes;

/// <summary>
///     This class is an in-memory <see cref="IDnsAccessControlRuleRepository" /> that counts calls
///     to <see cref="ListEnabledAsync" /> in <see cref="ListEnabledAsyncCallCount" />. The tests use
///     the count to show when <see cref="DnsAccessControlRuleCache" /> reads the repository.
/// </summary>
public sealed class CountingDnsAccessControlRuleRepository : IDnsAccessControlRuleRepository
{
    private readonly List<DnsAccessControlRuleEntity> _rules = [];

    private int _listEnabledAsyncCallCount;

    /// <summary>
    ///     This property contains how many times <see cref="ListEnabledAsync" /> has been called.
    ///     <see cref="DnsAccessControlRuleCache" /> calls it once per load or reload, and never
    ///     during a capability check.
    /// </summary>
    public int ListEnabledAsyncCallCount => _listEnabledAsyncCallCount;

    /// <summary>
    ///     This method adds a rule to the backing list. It does the same as <see cref="AddAsync" />
    ///     but reads as test setup rather than an application write.
    /// </summary>
    public void Seed(DnsAccessControlRuleEntity rule) => _rules.Add(rule);

    /// <inheritdoc />
    public Task<IReadOnlyList<DnsAccessControlRuleEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    )
    {
        Interlocked.Increment(ref _listEnabledAsyncCallCount);

        return Task.FromResult<IReadOnlyList<DnsAccessControlRuleEntity>>([
            .. _rules.Where(rule => !rule.IsDisabled),
        ]);
    }

    /// <inheritdoc />
    public Task<DnsAccessControlRuleEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(_rules.FirstOrDefault(rule => rule.Id == id));

    /// <inheritdoc />
    public Task<IReadOnlyList<DnsAccessControlRuleEntity>> ListAsync(
        Expression<Func<DnsAccessControlRuleEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsAccessControlRuleEntity>>(
            predicate is null ? [.. _rules] : [.. _rules.Where(predicate.Compile())]
        );

    /// <inheritdoc />
    public Task AddAsync(
        DnsAccessControlRuleEntity entity,
        CancellationToken stoppingToken = default
    )
    {
        _rules.Add(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Update(DnsAccessControlRuleEntity entity)
    {
        int index = _rules.FindIndex(rule => rule.Id == entity.Id);

        if (index >= 0)
            _rules[index] = entity;
    }

    /// <inheritdoc />
    public void Remove(DnsAccessControlRuleEntity entity) =>
        _rules.RemoveAll(rule => rule.Id == entity.Id);

    /// <inheritdoc />
    public Task<IReadOnlyList<DnsAccessControlRuleEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<DnsAccessControlRuleEntity>>([
            .. _rules.OrderBy(rule => rule.Name, StringComparer.Ordinal).Skip(skip).Take(take),
        ]);

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        Task.FromResult(_rules.Count);
}
