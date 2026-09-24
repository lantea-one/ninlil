using System.Linq.Expressions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

namespace Ninlil.Dns.Server.AccessControl.Tests.Fakes;

/// <summary>
///     This class stands in for a real <see cref="IDnsAccessControlRuleRepository" />, backed by
///     a plain in-memory list, with an <see cref="Interlocked" />-counted
///     <see cref="ListEnabledAsyncCallCount" /> -- the same "fake repository with a read counter
///     standing in for a real database" shape the original throwaway spike used to prove
///     <see cref="DnsAccessControlRuleCache" />'s hot-path read behavior, now committed as a real
///     test double so that proof can be re-run as a permanent, automated test rather than a
///     one-off console run.
/// </summary>
public sealed class CountingDnsAccessControlRuleRepository : IDnsAccessControlRuleRepository
{
    private readonly List<DnsAccessControlRuleEntity> _rules = [];

    private int _listEnabledAsyncCallCount;

    /// <summary>
    ///     This property contains how many times <see cref="ListEnabledAsync" /> has actually
    ///     been called -- the one call <see cref="DnsAccessControlRuleCache" /> is allowed to make
    ///     per load/reload, and the metric these tests assert never advances on an ordinary
    ///     capability check.
    /// </summary>
    public int ListEnabledAsyncCallCount => _listEnabledAsyncCallCount;

    /// <summary>
    ///     This method seeds a rule directly into this fake's backing list, bypassing
    ///     <see cref="AddAsync" /> -- used by a test to set up state before the cache's own
    ///     initial load, without that seeding itself counting as an application-level write.
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
