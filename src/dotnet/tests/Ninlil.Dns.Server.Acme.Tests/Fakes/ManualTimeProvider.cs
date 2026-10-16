// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests.Fakes;

/// <summary>
///     This class is a clock a test moves by hand, so TTL expiry and decrement can be asserted
///     exactly and without sleeping. Its timers (the ones <c>Task.Delay(TimeSpan, TimeProvider,
///     CancellationToken)</c> creates) fire only from <see cref="Advance" />, so a background loop
///     that waits on this clock moves only when the test says so.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     This property is how many timers have been created so far, fired or not. A test waits
    ///     on it to know a background loop has reached its next wait.
    /// </summary>
    public int TimersCreated { get; private set; }

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>
    ///     This method moves the clock forward and runs the callback of every timer that is due.
    ///     The callbacks run on the calling thread, after the lock is released.
    /// </summary>
    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;

        lock (_lock)
        {
            _now += by;
            due = [.. _timers.Where(timer => timer.DueAt <= _now)];

            foreach (ManualTimer timer in due)
                timer.Reschedule(_now);
        }

        foreach (ManualTimer timer in due)
            timer.Fire();
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

        lock (_lock)
        {
            timer.Change(dueTime, period);
            _timers.Add(timer);
            TimersCreated++;
        }

        return timer;
    }

    /// <summary>
    ///     This class is one timer of <see cref="ManualTimeProvider" />. It is due at a point on the
    ///     manual clock and, with a period, repeats from there.
    /// </summary>
    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object state)
        : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        /// <summary>
        ///     This property is when the timer next fires; <see cref="DateTimeOffset.MaxValue" />
        ///     when it will not.
        /// </summary>
        public DateTimeOffset DueAt { get; private set; } = DateTimeOffset.MaxValue;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            DueAt =
                dueTime == Timeout.InfiniteTimeSpan
                    ? DateTimeOffset.MaxValue
                    : owner._now + dueTime;
            return true;
        }

        /// <summary>
        ///     This method sets the next due point after a firing: one period on, or never.
        /// </summary>
        public void Reschedule(DateTimeOffset now) =>
            DueAt = _period == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : now + _period;

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner._lock)
            {
                DueAt = DateTimeOffset.MaxValue;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
