// Define our namespace.
namespace Ninlil.Dns.Data.Tests;

/// <summary>
///     This class is a clock the test moves by hand, so tests of the sticky window need no
///     sleeping. It counts in milliseconds.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long milliseconds = 1_000;

    /// <inheritdoc />
    public override long TimestampFrequency => 1_000;

    /// <inheritdoc />
    public override long GetTimestamp() => Interlocked.Read(ref milliseconds);

    /// <summary>
    ///     This method moves the clock forward.
    /// </summary>
    /// <param name="by">How far to move it.</param>
    public void Advance(TimeSpan by) =>
        Interlocked.Add(ref milliseconds, (long)by.TotalMilliseconds);
}
