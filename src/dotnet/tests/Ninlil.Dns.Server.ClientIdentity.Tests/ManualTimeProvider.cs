// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class is a clock tests move by hand, so time-to-live and expiry checks do not
///     depend on real elapsed time. Timers created from it never fire.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>
    ///     This method moves the clock forward.
    /// </summary>
    public void Advance(TimeSpan amount) => _now += amount;
}
