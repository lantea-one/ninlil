using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class tests that <see cref="ForwarderHealthTracker" /> keeps running when
///     <see cref="ForwardingOptions.HealthCheckInterval" /> holds a value
///     <c>Task.Delay</c> cannot wait for, as a hand-edited configuration file can leave behind.
///     The options reload live, and a loop that throws would stop the whole host. The tests start
///     the real background service against a fixed options monitor and a negative interval.
/// </summary>
public sealed class ForwarderHealthTrackerTests
{
    [Theory]
    [InlineData("-00:00:05", "00:00:01")]
    [InlineData("00:00:00", "00:00:01")]
    [InlineData("00:00:00.500", "00:00:01")]
    [InlineData("00:00:30", "00:00:30")]
    [InlineData("1.00:00:00", "1.00:00:00")]
    [InlineData("49.17:02:47.295", "1.00:00:00")]
    [InlineData("10675199.02:48:05", "1.00:00:00")]
    public void ClampProbeInterval_ForAnyConfiguredValue_ReturnsAValueTaskDelayAccepts(
        string configured,
        string expected
    ) =>
        Assert.Equal(
            TimeSpan.Parse(expected),
            ForwarderHealthTracker.ClampProbeInterval(TimeSpan.Parse(configured))
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WithANegativeHealthCheckInterval_KeepsRunning(
        bool probingEnabled
    )
    {
        // With probing off, the loop only waits; with it on, the loop waits and then probes.
        // Both paths pass the interval to Task.Delay. With probing off nothing catches its
        // exception, so an unchecked negative value would end the loop.
        ForwardingOptions options = new()
        {
            Enable = probingEnabled,
            FailoverStrategy = ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin,
            HealthCheckInterval = TimeSpan.FromSeconds(-5),
        };
        using ForwarderHealthTracker tracker = new(
            [],
            new StaticOptionsMonitor<ForwardingOptions>(options),
            NullLogger<ForwarderHealthTracker>.Instance
        );

        await tracker.StartAsync(CancellationToken.None);
        await Task.Delay(300);

        Assert.NotNull(tracker.ExecuteTask);
        Assert.False(tracker.ExecuteTask.IsCompleted);

        await tracker.StopAsync(CancellationToken.None);
    }
}
