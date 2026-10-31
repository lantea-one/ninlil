using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Wire.Ra;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra.Tests;

/// <summary>
///     This test class checks what <see cref="Icmpv6RaListenerHostedService" /> does at boot when
///     the raw ICMPv6 socket cannot be opened, which is what an unprivileged process gets (no
///     <c>CAP_NET_RAW</c>). The hosted service must not throw, because an exception out of a
///     hosted service stops the whole host. It logs one Error, the listener stays stopped and
///     keeps the reason for the health check.
///     <para>
///         <b>It needs a process without the raw-socket permission</b>, which is the normal case
///         for a test run. With the permission (root or <c>CAP_NET_RAW</c>) the failure cannot be
///         provoked, so the test returns at once and is reported as passed, as the live service
///         control test does for the opposite case.
///     </para>
/// </summary>
public sealed class Icmpv6RaListenerBindFailureTests
{
    [Fact]
    public async Task HostedServiceStart_WithoutPermissionForARawSocket_LogsOneErrorAndDoesNotThrow()
    {
        if (CanOpenRawIcmpv6Socket())
            return;

        RaProtocolOptions protocolOptions = new() { Enable = true };
        Icmpv6RaListener listener = new(
            new NoAdvertisementProvider(),
            Options.Create(protocolOptions),
            new CapturingLogger<Icmpv6RaListener>()
        );
        CapturingLogger<Icmpv6RaListenerHostedService> logger = new();
        Icmpv6RaListenerHostedService hosted = new(
            listener,
            Options.Create(protocolOptions),
            logger
        );

        await hosted.StartAsync(CancellationToken.None);

        Assert.False(listener.IsListening);
        Assert.False(string.IsNullOrWhiteSpace(listener.StartFailure));
        string error = Assert.Single(logger.Messages(LogLevel.Error));
        Assert.Contains("could not bind its socket", error);
        Assert.Contains(listener.StartFailure, error);
    }

    /// <summary>
    ///     This method reports whether this process may open a raw ICMPv6 socket, by trying.
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
    ///     This class is a provider with no content. The listener never asks it for anything,
    ///     because the start fails before any loop runs.
    /// </summary>
    private sealed class NoAdvertisementProvider : IRaAdvertisementProvider
    {
        /// <inheritdoc />
        public RouterAdvertisementMessage BuildCurrentAdvertisement(string interfaceName = null) =>
            null;
    }
}
