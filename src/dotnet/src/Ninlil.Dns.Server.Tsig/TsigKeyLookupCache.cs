using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig;

/// <summary>
///     This class holds every TSIG key in memory, so signing or verifying a message reads no
///     database row and opens no service scope. Every signed message, from a flood of
///     unauthenticated ones to each message of a long AXFR, needs its key. An unknown name is
///     answered from the same copy, so a sender with a made-up key name cannot turn the key table
///     into a query amplifier, and the copy grows with the number of stored keys, never with the
///     names senders invent. It is one <see cref="RefreshingSnapshot{T}" /> of a
///     <see cref="TsigKeySnapshot" />, shared by <see cref="TsigService" /> and
///     <see cref="TsigTransactionSignatureService" />.
///     <para>
///         <strong>Expiry.</strong> The cache subscribes to <see cref="IEntityChangeNotifier" /> and
///         expires the snapshot when a <see cref="TsigKeyEntity" /> is saved or removed. The next
///         lookup waits for a reload that starts after the change, so a key that is deleted,
///         disabled or changed through the API is refused or used as changed by the very next
///         message in this process. A change made elsewhere (another cluster node, a direct
///         database edit) shows after <see cref="Lifetime" />, plus the time one reload takes,
///         while the database answers. A reload for age alone that fails keeps the last good
///         copy, so such a change shows only once a reload succeeds.
///     </para>
///     <para>
///         <strong>Failure and storage.</strong> Every load reads the primary database, never a
///         replica: a replica that lags behind a deletion would bring the deleted key back. If the
///         first load fails, the copy is empty and every key is refused until a load succeeds. If
///         a reload that follows a change fails, the copy also becomes empty instead of keeping
///         the old keys, because the old copy may hold the key that was just deleted. A reload that
///         runs past the refresh timeout counts as failed. A refresh that only follows the age of
///         the copy keeps the last good copy when it fails or times out.
///     </para>
/// </summary>
public sealed class TsigKeyLookupCache : IDisposable
{
    /// <summary>
    ///     This constant contains how long a copy is used before a background refresh starts. It
    ///     bounds how long a change made outside this process takes to apply.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This field holds the keys, loaded and refreshed by <see cref="RefreshingSnapshot{T}" />.
    /// </summary>
    private readonly RefreshingSnapshot<TsigKeySnapshot> _snapshot;

    /// <summary>
    ///     This field holds the change-notification subscription, removed in <see cref="Dispose" />.
    /// </summary>
    private readonly IDisposable _subscription;

    /// <summary>
    ///     This constructor builds the cache and subscribes it to key changes.
    /// </summary>
    /// <param name="scopeFactory">The factory used to reach the key repository, once per load.</param>
    /// <param name="notifier">The signal that tells this cache when keys were saved.</param>
    /// <param name="logger">The logger load failures are recorded to.</param>
    /// <param name="timeProvider">The clock for the age of the copy.</param>
    public TsigKeyLookupCache(
        IServiceScopeFactory scopeFactory,
        IEntityChangeNotifier notifier,
        ILogger<TsigKeyLookupCache> logger,
        TimeProvider timeProvider
    )
    {
        _snapshot = new RefreshingSnapshot<TsigKeySnapshot>(
            (reason, token) => LoadAsync(scopeFactory, logger, reason, token),
            TsigKeySnapshot.Empty,
            Lifetime,
            timeProvider,
            logger,
            "TSIG keys"
        );

        // The callback only marks the copy out of date; the next lookup reloads it.
        _subscription = notifier.Subscribe([typeof(TsigKeyEntity)], _ => _snapshot.Expire());
    }

    /// <summary>
    ///     This method returns the key with the given name. Key names are stored in lowercase, but
    ///     rows written before that rule may keep their case, so the canonical lowercase name is
    ///     tried first and the name as given second. The first call waits for the first load.
    /// </summary>
    /// <param name="name">The key name from the message, with or without the trailing dot.</param>
    /// <param name="stoppingToken">The token used to stop waiting for a load.</param>
    /// <returns>The key, or null when no key has that name.</returns>
    public async ValueTask<TsigKeyEntity> FindAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => (await _snapshot.GetAsync(stoppingToken)).Find(name);

    /// <summary>
    ///     This method removes the change-notification subscription.
    /// </summary>
    public void Dispose() => _subscription.Dispose();

    /// <summary>
    ///     This method reads every key from the primary database in a short-lived scope and builds
    ///     the snapshot. A load that follows a change and fails, by an exception or by the refresh
    ///     timeout, returns the empty snapshot (see the class summary). A load that only follows the
    ///     age of the copy throws in both cases, so the last good copy stays.
    /// </summary>
    private static async Task<TsigKeySnapshot> LoadAsync(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        SnapshotLoadReasonEnum reason,
        CancellationToken stoppingToken
    )
    {
        // stoppingToken is the snapshot's refresh timeout (see RefreshingSnapshot<T>), not the
        // host's stopping token.
        try
        {
            // A plain scope reads the primary on purpose; see the class summary.
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IReadOnlyList<TsigKeyEntity> keys = await scope
                .ServiceProvider.GetRequiredService<ITsigKeyRepository>()
                .ListAsync(stoppingToken: stoppingToken);

            return new TsigKeySnapshot(keys);
        }
        catch (Exception exception) when (reason == SnapshotLoadReasonEnum.Expired)
        {
            // This includes the refresh timeout: a load that follows a change and runs out of
            // time has not shown that the deleted key is gone, so it is a failed reload too.
            // The token is only that timeout. The snapshot never hands the loader the host's
            // stopping token, so an exception here cannot be a shutdown being treated as a
            // failure.
            logger.LogError(
                exception,
                "Reloading the TSIG keys after a change failed; every TSIG key is refused until a reload succeeds."
            );
            return TsigKeySnapshot.Empty;
        }
    }
}

/// <summary>
///     This class is one immutable copy of every TSIG key, by stored name. It is built once per
///     load and never changed, so readers need no lock.
/// </summary>
public sealed class TsigKeySnapshot
{
    /// <summary>
    ///     This field is the copy used before the first load succeeds: no keys.
    /// </summary>
    public static readonly TsigKeySnapshot Empty = new([]);

    /// <summary>
    ///     This field indexes the keys by stored name, compared exactly: the lookup tries the
    ///     canonical lowercase name and the given spelling itself.
    /// </summary>
    private readonly Dictionary<string, TsigKeyEntity> _byName = new(StringComparer.Ordinal);

    /// <summary>
    ///     This constructor indexes the keys by their stored name; the first of two equal names
    ///     wins.
    /// </summary>
    /// <param name="keys">The keys to hold.</param>
    public TsigKeySnapshot(IEnumerable<TsigKeyEntity> keys)
    {
        foreach (TsigKeyEntity key in keys)
            if (key.Name is not null)
                _byName.TryAdd(key.Name, key);
    }

    /// <summary>
    ///     This method finds a key: the canonical lowercase name first, then the name as given
    ///     (without its trailing dot), which finds a legacy row that kept its case.
    /// </summary>
    /// <param name="name">The key name, with or without the trailing dot.</param>
    /// <returns>The key, or null when no key has that name.</returns>
    public TsigKeyEntity Find(string name)
    {
        string exact = name.TrimEnd('.');

        if (_byName.TryGetValue(exact.ToLowerInvariant(), out TsigKeyEntity key))
            return key;

        return _byName.TryGetValue(exact, out key) ? key : null;
    }
}
