// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class is a clock that only moves when a test sets or advances it. The TSIG tests
///     register it in place of <see cref="TimeProvider.System" /> so Time Signed, the Fudge window
///     and the key lookup cache lifetime are all deterministic. The monotonic timestamp follows
///     the same hand-moved clock, in ticks of 100 ns, so the cache, which measures the age of its
///     copy with <see cref="TimeProvider.GetElapsedTime(long)" />, ages when a test advances the
///     clock. Timers created from it, such as the refresh timeout of the cache, fire when
///     <see cref="Advance" /> or <see cref="Set" /> moves the clock to or past their due time, never
///     by real elapsed time.
/// </summary>
/// <param name="start">The time the clock starts at.</param>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <summary>
    ///     This field holds the timers that have not been disposed, guarded by itself.
    /// </summary>
    private readonly List<ManualTimer> _timers = [];

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => _now.UtcTicks;

    /// <summary>
    ///     This method moves the clock forward (or back, with a negative value).
    /// </summary>
    public void Advance(TimeSpan by)
    {
        _now += by;
        FireDueTimers();
    }

    /// <summary>
    ///     This method sets the clock to an exact instant.
    /// </summary>
    public void Set(DateTimeOffset now)
    {
        _now = now;
        FireDueTimers();
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(
        TimerCallback callback,
        object state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        ManualTimer timer = new(this, callback, state);
        timer.Change(dueTime, period);

        lock (_timers)
            _timers.Add(timer);

        return timer;
    }

    /// <summary>
    ///     This method runs the callback of every timer whose due time has been reached. The
    ///     callbacks run on the calling thread, outside the lock, so a callback may dispose a timer.
    /// </summary>
    private void FireDueTimers()
    {
        ManualTimer[] snapshot;
        lock (_timers)
            snapshot = [.. _timers];

        foreach (ManualTimer timer in snapshot)
            timer.FireIfDue();
    }

    /// <summary>
    ///     This method forgets a disposed timer.
    /// </summary>
    private void Remove(ManualTimer timer)
    {
        lock (_timers)
            _timers.Remove(timer);
    }

    /// <summary>
    ///     This class is a timer on the hand-moved clock: it fires from
    ///     <see cref="ManualTimeProvider.FireDueTimers" />, once per due time (a period re-arms it).
    /// </summary>
    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object state)
        : ITimer
    {
        private readonly object _gate = new();
        private DateTimeOffset? _due;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        /// <inheritdoc />
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                _period = period;
            }

            return true;
        }

        /// <summary>
        ///     This method runs the callback when the clock has reached the due time.
        /// </summary>
        public void FireIfDue()
        {
            lock (_gate)
            {
                if (_due is null || owner._now < _due)
                    return;

                _due = _period == Timeout.InfiniteTimeSpan ? null : owner._now + _period;
            }

            callback(state);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_gate)
                _due = null;

            owner.Remove(this);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
