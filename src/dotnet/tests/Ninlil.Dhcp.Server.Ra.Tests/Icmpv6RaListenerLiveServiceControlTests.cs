using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
using Ninlil.Dhcp.Common.Wire.Ra;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This test class checks that <see cref="Icmpv6RaListener" /> can be started and stopped
///     repeatedly, like <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4LiveServiceControlTests</c> and
///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Tests.Dhcpv6LiveServiceControlTests</c>: start, solicit and
///     receive an advertisement over IPv6 loopback, stop, confirm silence, start again, and
///     receive an advertisement again.
///     <para>
///         <b>It needs a raw ICMPv6 socket</b> (<c>CAP_NET_RAW</c> or root), which CI runners
///         usually do not grant (see <see cref="Icmpv6RaListener" />). The test first tries to
///         open one with <see cref="CanOpenRawIcmpv6Socket" /> and, if that fails, returns at once
///         and is reported as passed rather than skipped, as
///         <c>Ninlil.Dns.Protocol.DnsOverQuic.Tests.DnsOverQuicListenerTests</c> does for QUIC. Run
///         with root or <c>CAP_NET_RAW</c>, it performs the full round trip.
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

        // First cycle: start and confirm a solicitation gets an advertisement.
        await listener.StartAsync(CancellationToken.None);
        Assert.True(listener.IsListening);
        Assert.True(await TrySolicitAndReceiveAdvertisementAsync());

        // Starting again while running must do nothing.
        await listener.StartAsync(CancellationToken.None);
        Assert.True(listener.IsListening);

        // After a stop, a solicitation must get no advertisement.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);
        Assert.False(await TrySolicitAndReceiveAdvertisementAsync());

        // Stopping again while stopped must also do nothing.
        await listener.StopAsync(CancellationToken.None);
        Assert.False(listener.IsListening);

        // Second cycle, which works only if StopAsync released the socket rather than just
        // stopping the loops.
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

    [Fact]
    public async Task Listener_WithAProviderThatFailsAtFirst_AnswersOnceItRecovers_AndStopsCleanly()
    {
        if (!CanOpenRawIcmpv6Socket())
            return;

        FlakyProvider provider = new() { Failing = true };
        Icmpv6RaListener listener = new(
            provider,
            Options.Create(new RaProtocolOptions { Enable = true }),
            NullLogger<Icmpv6RaListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);

        try
        {
            // While the provider throws, nothing is answered but the loops keep running.
            Assert.False(await TrySolicitAndReceiveAdvertisementAsync());
            Assert.True(((IDhcpProtocolService)listener).IsRunning);

            provider.Failing = false;
            Assert.True(await TrySolicitAndReceiveAdvertisementAsync());
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }

        Assert.False(listener.IsListening);
    }

    /// <summary>
    ///     This class is a provider that throws while <see cref="Failing" /> is set.
    /// </summary>
    private sealed class FlakyProvider : IRaAdvertisementProvider
    {
        public volatile bool Failing;

        public RouterAdvertisementMessage BuildCurrentAdvertisement(string interfaceName = null) =>
            Failing
                ? throw new InvalidOperationException("The provider failed.")
                : new RouterAdvertisementMessage { RouterLifetime = TimeSpan.FromSeconds(1800) };
    }

    /// <summary>
    ///     This method reports whether the process can open a raw ICMPv6 socket, by trying to,
    ///     rather than guessing from the platform or a setting.
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
    ///     This method sends one encoded Router Solicitation over IPv6 loopback and reports
    ///     whether a Router Advertisement arrived within a short timeout; a timeout returns
    ///     <see langword="false" /> rather than throwing. A raw ICMPv6 socket receives every ICMPv6
    ///     message on the host, possibly including its own solicitation, so it keeps reading
    ///     until an advertisement arrives or time runs out.
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

                // Something else arrived (our own solicitation, or other Neighbor Discovery
                // traffic); keep waiting until the timeout.
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This class is an <see cref="IOptionsMonitor{TOptions}" /> fake that always returns the
    ///     same value, like <c>RaAdvertisementProviderTests.FixedOptionsMonitor</c>.
    /// </summary>
    private sealed class FixedOptionsMonitor(RaOptions value) : IOptionsMonitor<RaOptions>
    {
        public RaOptions CurrentValue { get; } = value;

        public RaOptions Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<RaOptions, string> listener) => null;
    }
}
