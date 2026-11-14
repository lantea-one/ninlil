using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.ClientIdentity;

/// <summary>
///     This class tests that <see cref="RefreshingSnapshot{T}" /> measures the age of its value
///     with the monotonic <see cref="TimeProvider.GetTimestamp" /> counter and not with the wall
///     clock, using a clock whose wall time never moves.
/// </summary>
public sealed class RefreshingSnapshotMonotonicClockTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task GetAsync_WhenOnlyTheTimestampAdvancesPastTheTimeToLive_StartsARefresh()
    {
        TimestampOnlyTimeProvider time = new();
        int loads = 0;
        RefreshingSnapshot<string> snapshot = new(
            _ => Task.FromResult("v" + Interlocked.Increment(ref loads)),
            "empty",
            Ttl,
            time,
            NullLogger.Instance,
            "test values"
        );
        Assert.Equal("v1", await snapshot.GetAsync());

        time.Advance(Ttl);
        await snapshot.GetAsync();
        await snapshot.WhenIdleAsync();

        Assert.Equal(2, loads);
        Assert.Equal("v2", await snapshot.GetAsync());
    }

    [Fact]
    public async Task GetAsync_WhenTheTimestampAdvancesLessThanTheTimeToLive_StartsNoRefresh()
    {
        TimestampOnlyTimeProvider time = new();
        int loads = 0;
        RefreshingSnapshot<string> snapshot = new(
            _ => Task.FromResult("v" + Interlocked.Increment(ref loads)),
            "empty",
            Ttl,
            time,
            NullLogger.Instance,
            "test values"
        );
        await snapshot.GetAsync();

        time.Advance(Ttl - TimeSpan.FromSeconds(1));
        await snapshot.GetAsync();
        await snapshot.WhenIdleAsync();

        Assert.Equal(1, loads);
    }

    /// <summary>
    ///     This class is a clock whose wall time is fixed and whose monotonic timestamp a test
    ///     moves by hand, in milliseconds. It proves that a reader consults the timestamp.
    /// </summary>
    private sealed class TimestampOnlyTimeProvider : TimeProvider
    {
        private long _milliseconds;

        /// <inheritdoc />
        public override long TimestampFrequency => 1_000;

        /// <inheritdoc />
        public override long GetTimestamp() => Interlocked.Read(ref _milliseconds);

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        /// <summary>
        ///     This method moves the monotonic timestamp forward; the wall time stays put.
        /// </summary>
        public void Advance(TimeSpan amount) =>
            Interlocked.Add(ref _milliseconds, (long)amount.TotalMilliseconds);
    }
}
