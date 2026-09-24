using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Wire.Ra;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This test proves <see cref="Icmpv6RaListener" /> is a genuinely restartable
///     <c>Ninlil.Dhcp.Common.Control.IDhcpProtocolService</c> -- the Router Advertisement analogue
///     of <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4LiveServiceControlTests</c>/
///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Tests.Dhcpv6LiveServiceControlTests</c>, with the same real
///     start -&gt; solicit -&gt; real advertisement -&gt; stop -&gt; confirm-silent -&gt;
///     start-again -&gt; real advertisement-again cycle.
///     <para>
///         <b>This transport needs a real raw ICMPv6 socket</b> (<c>CAP_NET_RAW</c> or root), which
///         a typical unprivileged CI runner does not grant -- see <see cref="Icmpv6RaListener" />'s
///         own doc comment. <see cref="CanOpenRawIcmpv6Socket" /> is checked once, at the top of
///         this test, via a real attempted socket creation; when it reports
///         <see langword="false" />, this test exits early, reported as passed rather than
///         skipped, mirroring <c>Ninlil.Dns.Protocol.DnsOverQuic.Tests.DnsOverQuicListenerTests</c>'
///         own established convention for an environment-dependent capability this repository has
///         no control over. Run with root (or an explicit <c>CAP_NET_RAW</c> grant), this test
///         performs the same genuine, real-socket round trip every other live-service-control test
///         in this project does.
///     </para>
/// </summary>
public sealed class Icmpv6RaListenerLiveServiceControlTests
{
    [Fact]
    public async Task Listener_CanBeStoppedAndRestartedLive_AndAnswersRealSolicitationsWhileRunning()
    {
        if (!CanOpenRawIcmpv6Socket())
            return;

        RaOptions raOptions = new() { RouterLifetime = TimeSpan.FromSeconds(1800) };
        RaAdvertisementProvider advertisementProvider = new(new FixedOptionsMonitor(raOptions));
        RaProtocolOptions protocolOptions = new() { Enable = true };
        Icmpv6RaListener listener = new(
            advertisementProvider,
            Options.Create(protocolOptions),
            NullLogger<Icmpv6RaListener>.Instance
        );

        // First cycle: start, confirm a real solicitation gets a real advertisement reply.
        await listener.StartAsync(CancellationToken.None);
        Assert.True(listener.IsListening);
        Assert.True(await TrySolicitAndReceiveAdvertisementAsync());

        // A second Start call while already running must be a safe no-op.
        await listener.StartAsync(CancellationToken.None);
        Assert.True(listener.IsListening);

        // Stop: the raw socket must genuinely be released -- a solicitation sent while stopped
        // must receive no advertisement at all.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);
        Assert.False(await TrySolicitAndReceiveAdvertisementAsync());

        // A second Stop call while already stopped must also be a safe no-op.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);

        // Second cycle: start again -- this only succeeds if StopAsync genuinely released the raw
        // socket rather than merely stopping the receive/send loops.
        await listener.StartAsync(CancellationToken.None);

        try
        {
            Assert.True(listener.IsListening);
            Assert.True(await TrySolicitAndReceiveAdvertisementAsync());
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    ///     This method reports whether the current process can genuinely open a raw ICMPv6 socket
    ///     right now -- the one real, environment-dependent precondition this whole test depends
    ///     on, checked via a real attempted socket creation rather than inferred from the
    ///     operating system or a configuration flag.
    /// </summary>
    private static bool CanOpenRawIcmpv6Socket()
    {
        try
        {
            using Socket probe = new(
                AddressFamily.InterNetworkV6,
                SocketType.Raw,
                ProtocolType.IcmpV6
            );
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method sends one real, wire-encoded Router Solicitation over the IPv6 loopback
    ///     address and reports whether a real Router Advertisement reply was received within a
    ///     short, bounded timeout -- <see langword="true" /> when the listener is genuinely up and
    ///     answering, <see langword="false" /> when nothing answers at all (the listener is
    ///     stopped), never throwing on the timeout case. A raw ICMPv6 socket delivers every ICMPv6
    ///     message on the host -- including, potentially, a loopback echo of this method's own
    ///     solicitation -- so this keeps receiving until either a genuine advertisement arrives or
    ///     the timeout elapses, rather than trusting the very first datagram received.
    /// </summary>
    private static async Task<bool> TrySolicitAndReceiveAdvertisementAsync()
    {
        using Socket client = new(
            AddressFamily.InterNetworkV6,
            SocketType.Raw,
            ProtocolType.IcmpV6
        );
        client.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));

        byte[] solicitBytes = RaMessageWriter.WriteRouterSolicitation(
            new RouterSolicitationMessage()
        );
        await client.SendToAsync(solicitBytes, new IPEndPoint(IPAddress.IPv6Loopback, 0));

        byte[] receiveBuffer = new byte[1500];
        using CancellationTokenSource receiveTimeout = new(TimeSpan.FromSeconds(2));

        try
        {
            while (true)
            {
                SocketReceiveFromResult result = await client.ReceiveFromAsync(
                    receiveBuffer,
                    new IPEndPoint(IPAddress.IPv6Any, 0),
                    receiveTimeout.Token
                );

                if (
                    RaMessageReader.TryReadRouterAdvertisement(
                        receiveBuffer.AsSpan(0, result.ReceivedBytes),
                        out _,
                        out _
                    )
                )
                    return true;

                // Something else arrived (most plausibly, a loopback echo of this method's own
                // outgoing solicitation, or unrelated Neighbor Discovery traffic already on this
                // host) -- keep waiting within the same bounded timeout window.
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This class is a minimal <see cref="IOptionsMonitor{TOptions}" /> stand-in that always
    ///     reports the same fixed value -- this test has no need to observe a live configuration
    ///     reload mid-run, the same reasoning
    ///     <c>RaAdvertisementProviderTests.FixedOptionsMonitor</c>'s own doc comment gives.
    /// </summary>
    private sealed class FixedOptionsMonitor(RaOptions value) : IOptionsMonitor<RaOptions>
    {
        public RaOptions CurrentValue { get; } = value;

        public RaOptions Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<RaOptions, string> listener) => null;
    }
}
