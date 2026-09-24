namespace Ninlil.Dns.Server.Forwarding.Tests.Fakes;

/// <summary>
///     This class is a clock a test moves by hand, so TTL expiry and decrement can be asserted
///     exactly and without sleeping.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
