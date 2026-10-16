using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Wire.Ra;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This test class checks when <see cref="Icmpv6RaListener" /> sends: the per-interface
///     multicast window of RFC 4861 Section 6.2.6, the deferral of a solicited multicast reply,
///     and the unicast window. It drives <c>RunUnsolicitedPassAsync</c> and
///     <c>AnswerSolicitationAsync</c> directly with a recording sender, a fixed interface list
///     (indexes 2 and 3) and a <see cref="ManualTimeProvider" />, so no raw socket, privilege or
///     wall-clock wait is involved.
/// </summary>
public sealed class Icmpv6RaListenerSchedulingTests
{
    private static readonly IPAddress SolicitationFromNowhere = IPAddress.IPv6Any;

    private static readonly IPAddress SolicitingHost = IPAddress.Parse("fe80::1%2");

    [Fact]
    public async Task Pass_WithTwoDueInterfaces_SendsAMulticastAdvertisementOnBoth()
    {
        ManualTimeProvider time = new();
        RecordingSender sender = new(time);
        Icmpv6RaListener listener = CreateListener(time);

        await listener.RunUnsolicitedPassAsync(sender, [], CancellationToken.None);

        Assert.Equal(2, sender.Sends.Count);
        Assert.All(
            sender.Sends,
            send => Assert.Equal(RaMulticastGroups.AllNodes, send.Destination)
        );
        Assert.Equal([2, 3], sender.Sends.Select(send => send.InterfaceIndex));
        Assert.Single(sender.Sends.Select(send => send.Time).Distinct());
    }

    [Fact]
    public async Task Pass_AfterAUnicastReplyOneSecondEarlier_StillSendsThePeriodicAdvertisement()
    {
        ManualTimeProvider time = new();
        RecordingSender sender = new(time);
        Icmpv6RaListener listener = CreateListener(time);

        await Answer(listener, sender, time, SolicitingHost);
        Assert.Single(sender.Sends);

        time.Advance(TimeSpan.FromSeconds(1));
        await listener.RunUnsolicitedPassAsync(sender, [], CancellationToken.None);

        Assert.Equal(
            [2, 3],
            sender
                .Sends.Where(send => send.InterfaceIndex is not null)
                .Select(send => send.InterfaceIndex)
        );
    }

    [Fact]
    public async Task Pass_InsideAnInterfacesWindow_RetriesWhenTheWindowEnds()
    {
        ManualTimeProvider time = new();
        RecordingSender sender = new(time);
        Icmpv6RaListener listener = CreateListener(time);
        Dictionary<int, DateTimeOffset> due = [];

        // Interface 2 sent a multicast advertisement just now, so its window is open.
        listener.SendWindow.TryReserveMulticastNow(2, out _);

        await listener.RunUnsolicitedPassAsync(sender, due, CancellationToken.None);

        Assert.Equal([3], sender.Sends.Select(send => send.InterfaceIndex));
        Assert.Equal(time.GetUtcNow() + TimeSpan.FromSeconds(3), due[2]);

        time.Advance(TimeSpan.FromSeconds(3));
        await listener.RunUnsolicitedPassAsync(sender, due, CancellationToken.None);

        Assert.Equal([3, 2], sender.Sends.Select(send => send.InterfaceIndex));
    }

    [Fact]
    public async Task Answer_ToTheUnspecifiedAddressInsideAnInterfacesWindow_IsDeferredToTheEndOfTheWindow()
    {
        ManualTimeProvider time = new();
        RecordingSender sender = new(time);
        Icmpv6RaListener listener = CreateListener(time);

        // A multicast advertisement left interface 2 at t.
        DateTimeOffset start = time.GetUtcNow();
        listener.SendWindow.TryReserveMulticastNow(2, out _);
        time.Advance(TimeSpan.FromSeconds(1));

        Task answer = listener.AnswerSolicitationAsync(
            sender,
            SolicitationFromNowhere,
            CancellationToken.None
        );

        // The random delay (at most 500 ms) ends; interface 3 has no open window and answers at once.
        await WaitForTimer(time, answer);
        time.Advance(TimeSpan.FromMilliseconds(500));
        await WaitUntil(() => sender.Sends.Count == 1);

        Assert.Equal(3, sender.Sends[0].InterfaceIndex);
        Assert.Equal(start + TimeSpan.FromMilliseconds(1500), sender.Sends[0].Time);

        // Interface 2 waits for the end of its window, t + 3 s, and then answers.
        Assert.False(answer.IsCompleted);
        await WaitForTimer(time, answer);
        time.Advance(TimeSpan.FromMilliseconds(1500));
        await answer.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, sender.Sends.Count);
        Assert.Equal(2, sender.Sends[1].InterfaceIndex);
        Assert.Equal(start + TimeSpan.FromSeconds(3), sender.Sends[1].Time);
    }

    [Fact]
    public async Task Answer_ToTwoUnicastSolicitationsFromOneHost_SendsOneReply()
    {
        ManualTimeProvider time = new();
        RecordingSender sender = new(time);
        Icmpv6RaListener listener = CreateListener(time);

        await Answer(listener, sender, time, SolicitingHost);
        time.Advance(TimeSpan.FromSeconds(1));
        await Answer(listener, sender, time, SolicitingHost);

        Assert.Single(sender.Sends);
        Assert.Equal(SolicitingHost, sender.Sends[0].Destination);
        Assert.Null(sender.Sends[0].InterfaceIndex);
    }

    [Fact]
    public async Task Answer_ToUnicastSolicitationsFromTwoHosts_SendsBothReplies()
    {
        ManualTimeProvider time = new();
        RecordingSender sender = new(time);
        Icmpv6RaListener listener = CreateListener(time);
        IPAddress other = IPAddress.Parse("fe80::2%2");

        await Answer(listener, sender, time, SolicitingHost);
        await Answer(listener, sender, time, other);

        Assert.Equal([SolicitingHost, other], sender.Sends.Select(send => send.Destination));
    }

    [Fact]
    public async Task StartAsync_ForgetsTheWindowsOfThePreviousRun()
    {
        ManualTimeProvider time = new();
        RecordingSender sender = new(time);
        Icmpv6RaListener listener = CreateListener(time);
        listener.InterfaceEnumerator = () => [];
        listener.SenderFactory = _ => sender;
        listener.SendWindow.TryReserveMulticastNow(2, out _);

        try
        {
            await listener.StartAsync();
        }
        catch (SocketException)
        {
            // A raw ICMPv6 socket needs CAP_NET_RAW; without it the test returns, as the live
            // service test does. RaSendWindowTests covers Reset on its own.
            return;
        }

        try
        {
            Assert.True(listener.SendWindow.TryReserveMulticastNow(2, out _));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    /// <summary>
    ///     This method answers one solicitation and moves the clock past the random delay (at most
    ///     500 ms), then waits for the answer to finish.
    /// </summary>
    private static async Task Answer(
        Icmpv6RaListener listener,
        RecordingSender sender,
        ManualTimeProvider time,
        IPAddress source
    )
    {
        Task answer = listener.AnswerSolicitationAsync(sender, source, CancellationToken.None);
        await WaitForTimer(time, answer);
        time.Advance(TimeSpan.FromMilliseconds(500));
        await answer.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    ///     This method waits until the task has armed a timer (it is waiting on the clock) or has
    ///     finished. The wait is a bounded poll, not a delay the test depends on.
    /// </summary>
    private static Task WaitForTimer(ManualTimeProvider time, Task task) =>
        WaitUntil(() => task.IsCompleted || time.PendingTimers > 0);

    /// <summary>
    ///     This method polls until the condition holds and fails the test after ten seconds.
    /// </summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition never became true.");
            await Task.Delay(1);
        }
    }

    /// <summary>
    ///     This method builds a listener on the manual clock whose interfaces are 2 and 3 and
    ///     whose provider always returns a small advertisement.
    /// </summary>
    private static Icmpv6RaListener CreateListener(ManualTimeProvider time) =>
        new(
            new FixedProvider(),
            Options.Create(new RaProtocolOptions { Enable = true }),
            NullLogger<Icmpv6RaListener>.Instance,
            time
        )
        {
            InterfaceEnumerator = () => [(2, "eth0"), (3, "eth1")],
        };

    /// <summary>
    ///     This class is a provider that returns the same advertisement for every interface.
    /// </summary>
    private sealed class FixedProvider : IRaAdvertisementProvider
    {
        public RouterAdvertisementMessage BuildCurrentAdvertisement(string interfaceName = null) =>
            new() { RouterLifetime = TimeSpan.FromSeconds(1800) };
    }

    /// <summary>
    ///     This class is a sender that records the destination, the outgoing interface and the
    ///     clock time of every datagram instead of using a socket.
    /// </summary>
    private sealed class RecordingSender(ManualTimeProvider time) : IRaDatagramSender
    {
        private readonly List<(
            IPAddress Destination,
            int? InterfaceIndex,
            DateTimeOffset Time
        )> _sends = [];

        public List<(IPAddress Destination, int? InterfaceIndex, DateTimeOffset Time)> Sends
        {
            get
            {
                lock (_sends)
                    return _sends.ToList();
            }
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> datagram,
            IPAddress destination,
            int? interfaceIndex,
            CancellationToken stoppingToken
        )
        {
            lock (_sends)
                _sends.Add((destination, interfaceIndex, time.GetUtcNow()));

            return ValueTask.CompletedTask;
        }
    }
}
