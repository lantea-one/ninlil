using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This class holds an immutable in-memory copy of database rows for code on the DNS query
///     path, such as the static-address-mapping and DHCP-lease client identity resolvers. A
///     query reads <see cref="GetAsync" /> instead of querying the database, so many queries
///     can run at once without a database round-trip each.
///     <para>
///         The value is replaced as a whole by reference, never changed in place, so readers need
///         no lock. Only the first load is awaited by a reader. After that a read always returns
///         the current value at once: when the value is older than the time-to-live, or after
///         <see cref="Invalidate" />, one background refresh runs (further requests join it) and
///         readers keep the old value until it finishes. A refresh that fails keeps the last
///         good value and is logged; if the very first load fails, the empty value is used until
///         the next time-to-live expiry, so a database outage does not turn into one failing
///         query per DNS query.
///     </para>
///     <para>
///         <see cref="Expire" /> is the stricter signal for a known local write: the next
///         <see cref="GetAsync" /> waits for a load that starts after the call, so a change saved
///         through the API is visible to the very next read in this process. Readers that arrive
///         while that load runs wait for the same one. If the load fails, they get the last good
///         value and the failure is logged.
///     </para>
///     <para>
///         The loader is told why it runs (<see cref="SnapshotLoadReasonEnum" />) so it can pick
///         the database it reads. A refresh only because the value got old
///         (<see cref="SnapshotLoadReasonEnum.TimeToLive" />) may read a replica that is a few
///         seconds behind. The first load and every load that follows an <see cref="Expire" />
///         or an explicit refresh must read the primary: an expiry means this process just
///         wrote, and a replica may not have that write yet, so reloading from it would keep the
///         old rows until the next time-to-live refresh. <see cref="SnapshotLoadScopeExtension" />
///         applies that rule to a scope factory.
///     </para>
/// </summary>
/// <typeparam name="T">The immutable snapshot type.</typeparam>
/// <param name="loader">
///     Reads the rows and builds a new snapshot; called by one refresh at a time. It receives the
///     reason for the load.
/// </param>
/// <param name="emptyValue">The value used when the first load fails.</param>
/// <param name="timeToLive">How old the value may get before a background refresh starts.</param>
/// <param name="timeProvider">The clock for the time-to-live.</param>
/// <param name="logger">The logger refresh failures are recorded to.</param>
/// <param name="description">What the snapshot holds, for the log messages.</param>
public sealed class RefreshingSnapshot<T>(
    Func<SnapshotLoadReasonEnum, CancellationToken, Task<T>> loader,
    T emptyValue,
    TimeSpan timeToLive,
    TimeProvider timeProvider,
    ILogger logger,
    string description
)
    where T : class
{
    /// <summary>
    ///     This constructor creates a snapshot whose loader does not care why it runs, such as one
    ///     that reads no database.
    /// </summary>
    /// <param name="loader">Reads the rows and builds a new snapshot; called by one refresh at a time.</param>
    /// <param name="emptyValue">The value used when the first load fails.</param>
    /// <param name="timeToLive">How old the value may get before a background refresh starts.</param>
    /// <param name="timeProvider">The clock for the time-to-live.</param>
    /// <param name="logger">The logger refresh failures are recorded to.</param>
    /// <param name="description">What the snapshot holds, for the log messages.</param>
    public RefreshingSnapshot(
        Func<CancellationToken, Task<T>> loader,
        T emptyValue,
        TimeSpan timeToLive,
        TimeProvider timeProvider,
        ILogger logger,
        string description
    )
        : this(
            (_, stoppingToken) => loader(stoppingToken),
            emptyValue,
            timeToLive,
            timeProvider,
            logger,
            description
        ) { }

    /// <summary>
    ///     This field is the longest one refresh may run before it is abandoned.
    /// </summary>
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This field guards the refresh bookkeeping below. It is held only for field updates,
    ///     never across a database call.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     This field is the current value and when it was loaded. It is replaced as a whole.
    /// </summary>
    private volatile Entry _entry;

    /// <summary>
    ///     This field counts calls to <see cref="Expire" />. It is written under
    ///     <see cref="_gate" />.
    /// </summary>
    private long _expireGeneration;

    /// <summary>
    ///     This field is the value of <see cref="_expireGeneration" /> when the most recently
    ///     finished load started. While it is below <see cref="_expireGeneration" /> the value is
    ///     known to be out of date and readers wait for a new load.
    /// </summary>
    private long _servedGeneration;

    /// <summary>
    ///     This field is the running refresh loop, or <see langword="null" /> when idle.
    /// </summary>
    private Task _running;

    /// <summary>
    ///     This field says that a refresh was requested while one was running, so the loop must
    ///     load again. It makes a change that lands mid-load visible.
    /// </summary>
    private bool _runAgain;

    /// <summary>
    ///     This method returns the current value. The first call waits for the first load; later
    ///     calls return at once, starting a background refresh when the value is stale.
    /// </summary>
    /// <param name="stoppingToken">The token used to stop waiting for the first load.</param>
    public ValueTask<T> GetAsync(CancellationToken stoppingToken = default)
    {
        Entry entry = _entry;
        if (entry is null)
            return new ValueTask<T>(LoadFirstAsync(stoppingToken));

        if (Volatile.Read(ref _servedGeneration) < Volatile.Read(ref _expireGeneration))
            return new ValueTask<T>(LoadExpiredAsync(stoppingToken));

        if (timeProvider.GetUtcNow() - entry.LoadedAtUtc >= timeToLive)
            StartIfIdle();

        return new ValueTask<T>(entry.Value);
    }

    /// <summary>
    ///     This method marks the value out of date because the rows it was built from changed.
    ///     It does not load: the next <see cref="GetAsync" /> awaits one load that starts after
    ///     this call (readers arriving meanwhile join it), so that read sees the change. A load
    ///     that is already running and started earlier is followed by another one. This method is
    ///     cheap and never blocks, so it is safe to call from a change-notification callback.
    /// </summary>
    public void Expire()
    {
        lock (_gate)
            _expireGeneration++;
    }

    /// <summary>
    ///     This method marks the value out of date and starts a background refresh. Readers keep
    ///     the old value until it completes.
    /// </summary>
    public void Invalidate() => _ = RefreshAsync();

    /// <summary>
    ///     This method reloads the value and completes when a load that started after the call has
    ///     finished (it joins the running refresh and asks it to load once more). It never throws
    ///     for a load failure; the failure is logged and the old value kept.
    /// </summary>
    /// <param name="stoppingToken">The token used to stop waiting; it does not cancel the load.</param>
    public Task RefreshAsync(CancellationToken stoppingToken = default)
    {
        Task running;
        lock (_gate)
        {
            if (_running is null)
                _running = Task.Run(() => RunAsync(false));
            else
                _runAgain = true;

            running = _running;
        }

        return running.WaitAsync(stoppingToken);
    }

    /// <summary>
    ///     This method returns a task that completes when no refresh is running, so tests and
    ///     shutdown code can wait for a background refresh without guessing at timing.
    /// </summary>
    public Task WhenIdleAsync()
    {
        lock (_gate)
            return _running ?? Task.CompletedTask;
    }

    /// <summary>
    ///     This method starts a background refresh unless one is already running; an already
    ///     running one is not asked to repeat, since it will leave a fresh value.
    /// </summary>
    private void StartIfIdle()
    {
        lock (_gate)
            _running ??= Task.Run(() => RunAsync(true));
    }

    /// <summary>
    ///     This method waits for a load that started after the latest <see cref="Expire" /> and
    ///     returns the value it left, or the last good value when that load failed.
    /// </summary>
    private async Task<T> LoadExpiredAsync(CancellationToken stoppingToken)
    {
        Task running;
        lock (_gate)
        {
            // Another reader's load may have finished while this one waited for the lock.
            if (_servedGeneration >= _expireGeneration)
                return _entry.Value;

            // A running load that started before the expiry reruns by itself (see RunAsync), so
            // joining it is enough and concurrent readers share one load.
            _running ??= Task.Run(() => RunAsync(false));
            running = _running;
        }

        await running.WaitAsync(stoppingToken);
        return _entry.Value;
    }

    /// <summary>
    ///     This method waits for the first load and returns the value it produced.
    /// </summary>
    private async Task<T> LoadFirstAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);
        return _entry.Value;
    }

    /// <summary>
    ///     This method loads, then loads again for as long as a refresh was requested during the
    ///     load. It never throws.
    /// </summary>
    /// <param name="startedByTimeToLive">
    ///     True when the first load of this run only follows the age of the value. Every later
    ///     load of the run was requested by an <see cref="Expire" /> or an explicit refresh.
    /// </param>
    private async Task RunAsync(bool startedByTimeToLive)
    {
        while (true)
        {
            long startedAtGeneration;
            SnapshotLoadReasonEnum reason;
            lock (_gate)
            {
                startedAtGeneration = _expireGeneration;

                // An Expire that landed before the load started makes it an expired load even
                // when the age triggered the run.
                reason =
                    _entry is null ? SnapshotLoadReasonEnum.Initial
                    : startedByTimeToLive && _servedGeneration >= startedAtGeneration
                        ? SnapshotLoadReasonEnum.TimeToLive
                    : SnapshotLoadReasonEnum.Expired;
            }

            startedByTimeToLive = false;

            try
            {
                using CancellationTokenSource timeout = new(RefreshTimeout, timeProvider);
                T value = await loader(reason, timeout.Token);
                _entry = new Entry(value, timeProvider.GetUtcNow());
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Could not refresh the in-memory {Description}; keeping the previous copy.",
                    description
                );

                // Restart the time-to-live so a failing database is retried once per period, not
                // once per query. Without any earlier value, readers use the empty one.
                _entry = new Entry(_entry?.Value ?? emptyValue, timeProvider.GetUtcNow());
            }

            lock (_gate)
            {
                // A failed load also counts as served: readers get the last good value rather
                // than retrying on every query. The time-to-live retries it.
                _servedGeneration = startedAtGeneration;

                // Load again for a refresh requested mid-load, and for an Expire that landed
                // after this load started (its rows may predate the change).
                if (!_runAgain && _servedGeneration >= _expireGeneration)
                {
                    _running = null;
                    return;
                }

                _runAgain = false;
            }
        }
    }

    /// <summary>
    ///     This record is one loaded value and when it was loaded.
    /// </summary>
    private sealed record Entry(T Value, DateTimeOffset LoadedAtUtc);
}
