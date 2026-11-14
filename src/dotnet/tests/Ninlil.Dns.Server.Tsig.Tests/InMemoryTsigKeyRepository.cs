using System.Linq.Expressions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class is a minimal <see cref="ITsigKeyRepository" /> over a list of keys. It matches
///     names exactly, like a case-sensitive database column, and counts how many times
///     <see cref="ListAsync" /> ran so a test can tell whether <see cref="TsigKeyLookupCache" />
///     loaded its keys. Only <see cref="FindByNameAsync" /> and <see cref="ListAsync" /> work;
///     every other member throws.
/// </summary>
/// <param name="keys">The keys the repository holds. A test may change the list.</param>
public sealed class InMemoryTsigKeyRepository(List<TsigKeyEntity> keys) : ITsigKeyRepository
{
    /// <summary>
    ///     This property contains how many times <see cref="ListAsync" /> ran, which is how many
    ///     times the cache loaded its keys.
    /// </summary>
    public int LoadCount { get; private set; }

    /// <summary>
    ///     This property makes <see cref="ListAsync" /> throw, as a database outage does.
    /// </summary>
    public bool FailLoads { get; set; }

    /// <summary>
    ///     This property makes <see cref="ListAsync" /> wait until its token is cancelled and then
    ///     throw <see cref="OperationCanceledException" />, as a database call that hangs until the
    ///     cache's refresh timeout does.
    /// </summary>
    public bool HangLoads { get; set; }

    /// <summary>
    ///     This property completes when a <see cref="ListAsync" /> call has started to hang, so a
    ///     test advances the clock only once the cache is waiting.
    /// </summary>
    public TaskCompletionSource HangStarted { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     This method makes the next hang report through a fresh <see cref="HangStarted" />.
    /// </summary>
    public void ResetHangSignal() =>
        HangStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public Task<TsigKeyEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    )
    {
        return Task.FromResult(keys.FirstOrDefault(key => key.Name == name));
    }

    /// <inheritdoc />
    public Task<TsigKeyEntity> GetByIdAsync(Guid id, CancellationToken stoppingToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public async Task<IReadOnlyList<TsigKeyEntity>> ListAsync(
        Expression<Func<TsigKeyEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    )
    {
        LoadCount++;

        if (HangLoads)
        {
            HangStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        if (FailLoads)
            throw new InvalidOperationException("The database is down.");

        return [.. keys.Where((predicate ?? (_ => true)).Compile())];
    }

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
