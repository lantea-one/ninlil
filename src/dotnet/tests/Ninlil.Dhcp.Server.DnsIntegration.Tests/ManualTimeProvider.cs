// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration.Tests;

/// <summary>
///     This class is a clock tests move by hand, so time-to-live and expiry checks do not
///     depend on real elapsed time. Timers created from it never fire. The monotonic timestamp
///     follows the same hand-moved clock, in ticks of 100 ns, so code that measures age with
///     <see cref="TimeProvider.GetElapsedTime(long)" /> sees the same time as code that reads
///     <see cref="GetUtcNow" />.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => _now.UtcTicks;

    /// <summary>
    ///     This method moves the clock forward.
    /// </summary>
    public void Advance(TimeSpan amount) => _now += amount;
}
