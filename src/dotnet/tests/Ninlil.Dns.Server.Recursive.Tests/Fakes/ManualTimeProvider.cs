namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class is a clock a test moves by hand, so TTL expiry and decrement can be asserted
///     exactly and without sleeping. Its timers (and so <c>Task.Delay(delay, timeProvider)</c>)
///     fire only when <see cref="Advance" /> moves the clock past their due time.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     This constructor starts the clock at the default instant (1 September 2026, noon UTC),
    ///     or at <paramref name="start" /> when a test needs a different one, for example to match
    ///     the validity window of signatures it built.
    /// </summary>
    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        if (start is { } value)
            _now = value;
    }

    /// <summary>
    ///     This property contains how many armed timers are waiting for the clock to advance.
    /// </summary>
    public int PendingTimerCount
    {
        get
        {
            lock (_lock)
                return _timers.Count(timer => timer.IsArmed);
        }
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        ManualTimer timer = new(this, callback, state);
        lock (_lock)
            _timers.Add(timer);
        timer.Change(dueTime, period);

        return timer;
    }

    /// <summary>
    ///     This method moves the clock forward and fires every timer that has come due.
    /// </summary>
    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;

        lock (_lock)
        {
            _now += by;
            due = [.. _timers.Where(timer => timer.IsArmed && timer.DueAt <= _now)];
        }

        foreach (ManualTimer timer in due)
            timer.Fire();
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object state)
        : ITimer
    {
        public bool IsArmed { get; private set; }
        public DateTimeOffset DueAt { get; private set; }
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._lock)
            {
                IsArmed = dueTime != Timeout.InfiniteTimeSpan;
                DueAt = owner._now + (IsArmed ? dueTime : TimeSpan.Zero);
                _period = period;
            }

            return true;
        }

        public void Fire()
        {
            lock (owner._lock)
            {
                if (_period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero)
                    IsArmed = false;
                else
                    DueAt = owner._now + _period;
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (owner._lock)
            {
                IsArmed = false;
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
