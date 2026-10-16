// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests.Fakes;

/// <summary>
///     This class is a clock a test moves by hand, so a schedule that spans an hour or a day can
///     be driven pass by pass without sleeping. It has no timers: a test that uses it calls the
///     code under test directly and never waits on <c>Task.Delay</c>.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>This method moves the clock forward by <paramref name="by" />.</summary>
    public void Advance(TimeSpan by) => _now += by;
}
