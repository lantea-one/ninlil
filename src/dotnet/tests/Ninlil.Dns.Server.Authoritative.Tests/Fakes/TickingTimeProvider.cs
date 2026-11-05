// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Fakes;

/// <summary>
///     This class is a clock tests move by hand whose timers fire when the clock passes their due
///     time, so a <see cref="PeriodicTimer" /> built from it ticks on <see cref="Advance" />
///     instead of on elapsed time. <see cref="ManualTimeProvider" /> never fires its timers.
/// </summary>
public sealed class TickingTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    /// <summary>
    ///     This property is the number of timers created so far. A test waits for it to be above
    ///     zero before it moves the clock, because a hosted service creates its timer when its
    ///     background task starts, which can be after <c>StartAsync</c> returns.
    /// </summary>
    public int TimerCount
    {
        get
        {
            lock (_gate)
                return _timers.Count;
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(
        TimerCallback callback,
        object state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        FakeTimer timer = new(this, callback, state);
        timer.Change(dueTime, period);

        lock (_gate)
            _timers.Add(timer);

        return timer;
    }

    /// <summary>
    ///     This method moves the clock forward and fires every timer that comes due, in due order.
    ///     The clock stands at a timer's due time while its callback runs.
    /// </summary>
    /// <param name="amount">How far to move the clock.</param>
    public void Advance(TimeSpan amount)
    {
        DateTimeOffset target = GetUtcNow() + amount;

        while (true)
        {
            FakeTimer next;

            lock (_gate)
            {
                next = _timers
                    .Where(timer => timer.DueAtUtc is { } due && due <= target)
                    .OrderBy(timer => timer.DueAtUtc)
                    .FirstOrDefault();

                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.DueAtUtc!.Value;
            }

            next.Fire();
        }
    }

    /// <summary>
    ///     This class is one timer of <see cref="TickingTimeProvider" />.
    /// </summary>
    private sealed class FakeTimer(TickingTimeProvider owner, TimerCallback callback, object state)
        : ITimer
    {
        private TimeSpan _period;

        /// <summary>
        ///     This property is when the timer fires next, or <see langword="null" /> when it is off.
        /// </summary>
        public DateTimeOffset? DueAtUtc { get; private set; }

        /// <inheritdoc />
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            DueAtUtc = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
            return true;
        }

        /// <summary>
        ///     This method runs the callback and schedules the next tick, if the timer repeats.
        /// </summary>
        public void Fire()
        {
            DueAtUtc =
                _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero
                    ? null
                    : DueAtUtc + _period;
            callback(state);
        }

        /// <inheritdoc />
        public void Dispose() => DueAtUtc = null;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
