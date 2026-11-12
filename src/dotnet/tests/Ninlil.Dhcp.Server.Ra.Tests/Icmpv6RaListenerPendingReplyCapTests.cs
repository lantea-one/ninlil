using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Wire.Ra;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This test class checks the admission of Router Solicitations in
///     <see cref="Icmpv6RaListener" />: the cap of <c>MaxPendingSolicitedReplies</c> detached
///     replies, the coalescing of solicitations from <c>::</c> into one pending multicast reply
///     (RFC 4861 Section 6.2.6), and the release of a slot when a reply ends. It calls
///     <c>TryAdmitSolicitation</c> and <c>HandleSolicitationAsync</c> directly with a recording
///     sender and a <see cref="ManualTimeProvider" />, so it needs no raw socket, privilege or
///     wall-clock wait.
/// </summary>
public sealed class Icmpv6RaListenerPendingReplyCapTests
{
    private const int Cap = 16;

    [Fact]
    public void TryAdmitSolicitation_BelowTheCap_AdmitsEverySolicitation()
    {
        Icmpv6RaListener listener = CreateListener(new ManualTimeProvider(), out _);

        for (int host = 1; host <= Cap; host++)
            Assert.True(listener.TryAdmitSolicitation(Host(host)));
    }

    [Fact]
    public void TryAdmitSolicitation_AtTheCap_DropsFurtherSolicitationsAndLogsADebugEntry()
    {
        Icmpv6RaListener listener = CreateListener(
            new ManualTimeProvider(),
            out CapturingLogger<Icmpv6RaListener> logger
        );
        Fill(listener);

        Assert.False(listener.TryAdmitSolicitation(Host(Cap + 1)));
        Assert.False(listener.TryAdmitSolicitation(Host(Cap + 2)));

        List<string> dropped = logger.Messages(LogLevel.Debug);
        Assert.Equal(2, dropped.Count);
        Assert.All(dropped, message => Assert.Contains("too many replies are pending", message));
    }

    [Fact]
    public async Task HandleSolicitationAsync_WhenAReplyEnds_FreesASlotForTheNextSolicitation()
    {
        ManualTimeProvider time = new();
        Icmpv6RaListener listener = CreateListener(time, out _);
        RecordingSender sender = new();
        Fill(listener);
        Assert.False(listener.TryAdmitSolicitation(Host(Cap + 1)));

        // The reply of the first admitted host is sent and releases its slot.
        await Handle(listener, sender, time, Host(1));

        Assert.Single(sender.Destinations);
        Assert.True(listener.TryAdmitSolicitation(Host(Cap + 1)));
        Assert.False(listener.TryAdmitSolicitation(Host(Cap + 2)));
    }

    [Fact]
    public void TryAdmitSolicitation_ForRepeatedSolicitationsFromTheUnspecifiedAddress_CoalescesThemIntoOneSlot()
    {
        Icmpv6RaListener listener = CreateListener(new ManualTimeProvider(), out _);

        Assert.True(listener.TryAdmitSolicitation(IPAddress.IPv6Any));

        // Each later "::" solicitation joins the pending multicast reply and takes no slot,
        // so the cap still leaves room for Cap - 1 other replies.
        for (int repeat = 0; repeat < 5; repeat++)
            Assert.False(listener.TryAdmitSolicitation(IPAddress.IPv6Any));

        for (int host = 1; host < Cap; host++)
            Assert.True(listener.TryAdmitSolicitation(Host(host)));

        Assert.False(listener.TryAdmitSolicitation(Host(Cap)));
    }

    [Fact]
    public async Task HandleSolicitationAsync_ForTheMulticastReply_AllowsTheNextUnspecifiedAddressSolicitation()
    {
        ManualTimeProvider time = new();
        Icmpv6RaListener listener = CreateListener(time, out _);
        RecordingSender sender = new();
        Assert.True(listener.TryAdmitSolicitation(IPAddress.IPv6Any));
        Assert.False(listener.TryAdmitSolicitation(IPAddress.IPv6Any));

        await Handle(listener, sender, time, IPAddress.IPv6Any);

        Assert.Equal(2, sender.Destinations.Count);
        Assert.True(listener.TryAdmitSolicitation(IPAddress.IPv6Any));
    }

    [Fact]
    public async Task TryAdmitSolicitation_ForTheUnspecifiedAddressAtTheCap_DoesNotLeaveAMulticastReplyPending()
    {
        ManualTimeProvider time = new();
        Icmpv6RaListener listener = CreateListener(time, out _);
        RecordingSender sender = new();
        Fill(listener);

        // Dropped for the cap, so no multicast reply is pending and none may be assumed.
        Assert.False(listener.TryAdmitSolicitation(IPAddress.IPv6Any));

        await Handle(listener, sender, time, Host(1));

        Assert.True(listener.TryAdmitSolicitation(IPAddress.IPv6Any));
    }

    /// <summary>
    ///     This method admits <see cref="Cap" /> distinct link-local hosts, so every slot is taken.
    /// </summary>
    private static void Fill(Icmpv6RaListener listener)
    {
        for (int host = 1; host <= Cap; host++)
            Assert.True(listener.TryAdmitSolicitation(Host(host)));
    }

    private static IPAddress Host(int number) => IPAddress.Parse($"fe80::{number:x}%2");

    /// <summary>
    ///     This method runs the detached handler of one admitted solicitation, moves the clock
    ///     past the random delay (at most 500 ms) and waits for it to finish.
    /// </summary>
    private static async Task Handle(
        Icmpv6RaListener listener,
        RecordingSender sender,
        ManualTimeProvider time,
        IPAddress source
    )
    {
        Task handler = listener.HandleSolicitationAsync(sender, source, CancellationToken.None);

        DateTime deadline = DateTime.UtcNow.AddSeconds(10);

        while (!handler.IsCompleted && time.PendingTimers == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The handler never waited on the clock.");
            await Task.Delay(1);
        }

        time.Advance(TimeSpan.FromMilliseconds(500));
        await handler.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    ///     This method builds a listener on the manual clock with two usable interfaces and a
    ///     provider that returns a small advertisement.
    /// </summary>
    private static Icmpv6RaListener CreateListener(
        ManualTimeProvider time,
        out CapturingLogger<Icmpv6RaListener> logger
    )
    {
        logger = new CapturingLogger<Icmpv6RaListener>();
        return new Icmpv6RaListener(
            new FixedProvider(),
            Options.Create(new RaProtocolOptions { Enable = true }),
            logger,
            time
        )
        {
            InterfaceEnumerator = () => [(2, "eth0"), (3, "eth1")],
        };
    }

    /// <summary>
    ///     This class is a provider that returns the same advertisement for every interface.
    /// </summary>
    private sealed class FixedProvider : IRaAdvertisementProvider
    {
        public RouterAdvertisementMessage BuildCurrentAdvertisement(string interfaceName = null) =>
            new() { RouterLifetime = TimeSpan.FromSeconds(1800) };
    }

    /// <summary>
    ///     This class is a sender that records the destination of every datagram instead of
    ///     using a socket.
    /// </summary>
    private sealed class RecordingSender : IRaDatagramSender
    {
        private readonly List<IPAddress> _destinations = [];

        public List<IPAddress> Destinations
        {
            get
            {
                lock (_destinations)
                    return [.. _destinations];
            }
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> datagram,
            IPAddress destination,
            int? interfaceIndex,
            CancellationToken stoppingToken
        )
        {
            lock (_destinations)
                _destinations.Add(destination);

            return ValueTask.CompletedTask;
        }
    }
}
