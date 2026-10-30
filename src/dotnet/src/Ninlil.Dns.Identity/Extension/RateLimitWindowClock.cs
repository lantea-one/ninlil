using System.Collections.Concurrent;
using System.Diagnostics;

// Define our namespace.
namespace Ninlil.Dns.Identity.Extension;

/// <summary>
///     This class remembers when each fixed-window limiter of the admin sign-in limiter started,
///     so a refused request can say how long is left in its window. The ASP.NET Core fixed window
///     limiter reports only the whole window length as its retry time and does not expose how much
///     of the window has passed. A limiter replenishes on a timer that starts when it is created,
///     so the end of the current window is the creation time plus a whole number of windows.
///     <para>
///         <see cref="ServiceCollectionAdminLoginRateLimitingExtension" /> calls
///         <see cref="WindowStarted" /> from the limiter factory, which runs once when a client's
///         partition is created, and <see cref="Remaining" /> from the rejection handler.
///     </para>
/// </summary>
/// <param name="window">The window length every tracked limiter uses.</param>
internal sealed class RateLimitWindowClock(TimeSpan window)
{
    /// <summary>
    ///     This constant contains how many remembered partitions trigger a clean-up of old ones, so
    ///     a scan from many addresses cannot grow the table without limit.
    /// </summary>
    private const int PruneThreshold = 4096;

    private readonly ConcurrentDictionary<string, long> _starts = new();

    /// <summary>
    ///     This method records that a limiter was just created for a partition.
    /// </summary>
    /// <param name="policyName">The rate limiting policy the limiter belongs to.</param>
    /// <param name="partitionKey">The client network the limiter counts.</param>
    public void WindowStarted(string policyName, string partitionKey)
    {
        if (_starts.Count > PruneThreshold)
            Prune();

        _starts[$"{policyName}|{partitionKey}"] = Stopwatch.GetTimestamp();
    }

    /// <summary>
    ///     This method returns how long the partition's current window still has to run.
    /// </summary>
    /// <param name="policyName">The rate limiting policy the limiter belongs to.</param>
    /// <param name="partitionKey">The client network the limiter counts.</param>
    /// <returns>The time left, or <see langword="null" /> when the partition is not remembered (the caller then falls back to the whole window).</returns>
    public TimeSpan? Remaining(string policyName, string partitionKey)
    {
        if (
            window <= TimeSpan.Zero
            || !_starts.TryGetValue($"{policyName}|{partitionKey}", out long start)
        )
            return null;

        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        long completedWindows = elapsed.Ticks / window.Ticks;

        return TimeSpan.FromTicks(window.Ticks * (completedWindows + 1) - elapsed.Ticks);
    }

    /// <summary>
    ///     This method forgets partitions older than two windows. A limiter that stays busy longer
    ///     than that loses its entry and then reports the whole window, which is longer than needed
    ///     but never too short.
    /// </summary>
    private void Prune()
    {
        foreach (KeyValuePair<string, long> entry in _starts)
            if (Stopwatch.GetElapsedTime(entry.Value) > window * 2)
                _starts.TryRemove(entry);
    }
}
