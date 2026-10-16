using System.Net;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This test class checks <see cref="RaSendWindow" /> alone, with a <see cref="ManualTimeProvider" />
///     moved by hand: the per-interface multicast window, the deferred slots for solicited
///     multicast replies, the per-destination unicast window and its 256-entry bound.
/// </summary>
public sealed class RaSendWindowTests
{
    private static readonly TimeSpan Window = RaSendWindow.MinimumDelayBetweenAdvertisements;

    [Fact]
    public void TryReserveMulticastNow_InsideTheWindow_IsRefusedWithTheTimeLeft()
    {
        ManualTimeProvider time = new();
        RaSendWindow window = new(time);

        Assert.True(window.TryReserveMulticastNow(2, out TimeSpan none));
        Assert.Equal(TimeSpan.Zero, none);

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.False(window.TryReserveMulticastNow(2, out TimeSpan retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(2), retryAfter);

        time.Advance(TimeSpan.FromSeconds(2));

        Assert.True(window.TryReserveMulticastNow(2, out _));
    }

    [Fact]
    public void TryReserveMulticastNow_ForAnotherInterface_IsIndependent()
    {
        RaSendWindow window = new(new ManualTimeProvider());

        Assert.True(window.TryReserveMulticastNow(2, out _));
        Assert.True(window.TryReserveMulticastNow(3, out _));
        Assert.False(window.TryReserveMulticastNow(2, out _));
    }

    [Fact]
    public void ReserveNextMulticastSlot_InsideTheWindow_ReturnsTheTimeUntilTheWindowEnds()
    {
        ManualTimeProvider time = new();
        RaSendWindow window = new(time);

        Assert.Equal(TimeSpan.Zero, window.ReserveNextMulticastSlot(2));

        time.Advance(TimeSpan.FromSeconds(1));

        // The second reply waits for the end of the first one's window; a third one queues behind it.
        Assert.Equal(TimeSpan.FromSeconds(2), window.ReserveNextMulticastSlot(2));
        Assert.Equal(TimeSpan.FromSeconds(5), window.ReserveNextMulticastSlot(2));
        Assert.Equal(TimeSpan.Zero, window.ReserveNextMulticastSlot(3));
    }

    [Fact]
    public void TryReserveMulticastNow_AfterAReservedSlot_WaitsForTheSlotsWindow()
    {
        ManualTimeProvider time = new();
        RaSendWindow window = new(time);

        window.ReserveNextMulticastSlot(2);
        time.Advance(TimeSpan.FromSeconds(1));
        window.ReserveNextMulticastSlot(2);

        // The deferred reply will leave at t + 3 s, so the periodic send waits until t + 6 s.
        Assert.False(window.TryReserveMulticastNow(2, out TimeSpan retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(5), retryAfter);
    }

    [Fact]
    public void TryReserveUnicast_SameDestinationInsideTheWindow_IsRefused()
    {
        ManualTimeProvider time = new();
        RaSendWindow window = new(time);
        IPAddress host = IPAddress.Parse("fe80::1%2");

        Assert.True(window.TryReserveUnicast(host));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(window.TryReserveUnicast(host));
        Assert.True(window.TryReserveUnicast(IPAddress.Parse("fe80::2%2")));
        time.Advance(Window);
        Assert.True(window.TryReserveUnicast(host));
    }

    [Fact]
    public void TryReserveUnicast_DoesNotUseTheMulticastWindow()
    {
        RaSendWindow window = new(new ManualTimeProvider());

        Assert.True(window.TryReserveMulticastNow(2, out _));
        Assert.True(window.TryReserveUnicast(IPAddress.Parse("fe80::1%2")));
        Assert.False(window.TryReserveMulticastNow(2, out _));
    }

    [Fact]
    public void TryReserveUnicast_ForManyDestinations_TracksAtMostTheLimit()
    {
        ManualTimeProvider time = new();
        RaSendWindow window = new(time);
        int accepted = 0;

        for (int i = 0; i < 300; i++)
            if (window.TryReserveUnicast(IPAddress.Parse($"fe80::{i + 1:x}%2")))
                accepted++;

        Assert.Equal(RaSendWindow.MaximumUnicastDestinations, accepted);
        Assert.Equal(RaSendWindow.MaximumUnicastDestinations, window.TrackedUnicastDestinations);

        // Once the window has ended the old entries are pruned and new destinations fit again.
        time.Advance(Window);

        Assert.True(window.TryReserveUnicast(IPAddress.Parse("fe80::ffff%2")));
        Assert.Equal(1, window.TrackedUnicastDestinations);
    }

    [Fact]
    public void Reset_ForgetsEveryWindow()
    {
        RaSendWindow window = new(new ManualTimeProvider());
        IPAddress host = IPAddress.Parse("fe80::1%2");

        window.TryReserveMulticastNow(2, out _);
        window.TryReserveUnicast(host);

        window.Reset();

        Assert.True(window.TryReserveMulticastNow(2, out _));
        Assert.True(window.TryReserveUnicast(host));
    }
}
