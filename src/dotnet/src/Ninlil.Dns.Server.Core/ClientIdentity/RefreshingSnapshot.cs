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
/// </summary>
/// <typeparam name="T">The immutable snapshot type.</typeparam>
/// <param name="loader">Reads the rows and builds a new snapshot; called by one refresh at a time.</param>
/// <param name="emptyValue">The value used when the first load fails.</param>
/// <param name="timeToLive">How old the value may get before a background refresh starts.</param>
/// <param name="timeProvider">The clock for the time-to-live.</param>
/// <param name="logger">The logger refresh failures are recorded to.</param>
/// <param name="description">What the snapshot holds, for the log messages.</param>
public sealed class RefreshingSnapshot<T>(
    Func<CancellationToken, Task<T>> loader,
    T emptyValue,
    TimeSpan timeToLive,
    TimeProvider timeProvider,
    ILogger logger,
    string description
)
    where T : class
{
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

        if (timeProvider.GetUtcNow() - entry.LoadedAtUtc >= timeToLive)
            StartIfIdle();

        return new ValueTask<T>(entry.Value);
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
                _running = Task.Run(RunAsync);
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
            _running ??= Task.Run(RunAsync);
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
    private async Task RunAsync()
    {
        while (true)
        {
            try
            {
                using CancellationTokenSource timeout = new(RefreshTimeout, timeProvider);
                T value = await loader(timeout.Token);
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
                if (!_runAgain)
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
