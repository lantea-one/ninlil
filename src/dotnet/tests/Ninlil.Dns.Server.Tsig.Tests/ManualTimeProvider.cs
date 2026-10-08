// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class is a clock that only moves when a test sets or advances it. The TSIG tests
///     register it in place of <see cref="TimeProvider.System" /> so Time Signed, the Fudge window
///     and the key lookup cache lifetime are all deterministic.
/// </summary>
/// <param name="start">The time the clock starts at.</param>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>
    ///     This method moves the clock forward (or back, with a negative value).
    /// </summary>
    public void Advance(TimeSpan by) => _now += by;

    /// <summary>
    ///     This method sets the clock to an exact instant.
    /// </summary>
    public void Set(DateTimeOffset now) => _now = now;
}
