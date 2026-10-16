// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This class is a clock tests move by hand, so window and delay checks do not depend on real
///     elapsed time. It is copied from <c>Ninlil.Dhcp.Server.DnsIntegration.Tests</c> and adds
///     timers: <see cref="Advance" /> fires every timer that has come due, which lets
///     <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)" /> complete under test
///     control.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
            return _now;
    }

    /// <summary>
    ///     This property contains the number of timers that are armed and have not fired.
    /// </summary>
    public int PendingTimers
    {
        get
        {
            lock (_lock)
                return _timers.Count(timer => timer.IsArmed);
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
        ManualTimer timer = new(this, callback, state);

        lock (_lock)
            _timers.Add(timer);

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    ///     This method moves the clock forward and fires the timers that are now due, in order.
    /// </summary>
    public void Advance(TimeSpan amount)
    {
        List<ManualTimer> due;

        lock (_lock)
        {
            _now += amount;
            due = _timers.Where(timer => timer.IsArmed && timer.DueAt <= _now).ToList();
        }

        foreach (ManualTimer timer in due.OrderBy(timer => timer.DueAt))
            timer.Fire();
    }

    /// <summary>
    ///     This class is a one-shot timer (a period is ignored) that fires only when
    ///     <see cref="Advance" /> reaches its due time.
    /// </summary>
    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object state)
        : ITimer
    {
        public bool IsArmed { get; private set; }

        public DateTimeOffset DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._lock)
            {
                IsArmed = dueTime != Timeout.InfiniteTimeSpan;
                DueAt = owner._now + (IsArmed ? dueTime : TimeSpan.Zero);
            }

            return true;
        }

        public void Fire()
        {
            lock (owner._lock)
            {
                if (!IsArmed)
                    return;

                IsArmed = false;
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
