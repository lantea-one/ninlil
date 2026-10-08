using System.Linq.Expressions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class is a minimal <see cref="ITsigKeyRepository" /> over a list of keys. It matches
///     names exactly, like a case-sensitive database column, and counts lookups so a test can tell
///     whether <see cref="TsigKeyLookupCache" /> went to the repository. Only
///     <see cref="FindByNameAsync" /> works; every other member throws.
/// </summary>
/// <param name="keys">The keys the repository holds. A test may change the list.</param>
public sealed class InMemoryTsigKeyRepository(List<TsigKeyEntity> keys) : ITsigKeyRepository
{
    /// <summary>
    ///     This property contains how many times <see cref="FindByNameAsync" /> ran.
    /// </summary>
    public int LookupCount { get; private set; }

    /// <inheritdoc />
    public Task<TsigKeyEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    )
    {
        LookupCount++;
        return Task.FromResult(keys.FirstOrDefault(key => key.Name == name));
    }

    /// <inheritdoc />
    public Task<TsigKeyEntity> GetByIdAsync(Guid id, CancellationToken stoppingToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IReadOnlyList<TsigKeyEntity>> ListAsync(
        Expression<Func<TsigKeyEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task AddAsync(TsigKeyEntity entity, CancellationToken stoppingToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public void Update(TsigKeyEntity entity) => throw new NotSupportedException();

    /// <inheritdoc />
    public void Remove(TsigKeyEntity entity) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IReadOnlyList<TsigKeyEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<IReadOnlyList<TsigKeyEntity>> ListUnusedClusterInviteKeysAsync(
        CancellationToken stoppingToken = default
    ) => throw new NotSupportedException();
}
