// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class is a clock that only moves when a test sets or advances it. The TSIG tests
///     register it in place of <see cref="TimeProvider.System" /> so Time Signed, the Fudge window
///     and the key lookup cache lifetime are all deterministic. The monotonic timestamp follows
///     the same hand-moved clock, in ticks of 100 ns, so the cache, which measures the age of its
///     copy with <see cref="TimeProvider.GetElapsedTime(long)" />, ages when a test advances the
///     clock.
/// </summary>
/// <param name="start">The time the clock starts at.</param>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => _now.UtcTicks;

    /// <summary>
    ///     This method moves the clock forward (or back, with a negative value).
    /// </summary>
    public void Advance(TimeSpan by) => _now += by;

    /// <summary>
    ///     This method sets the clock to an exact instant.
    /// </summary>
    public void Set(DateTimeOffset now) => _now = now;
}
