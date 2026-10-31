using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Protocol.Ra;
using Ninlil.Dhcp.Server.Tftp;
using Ninlil.Web.HealthChecks;
using MsOptions = Microsoft.Extensions.Options.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="DhcpServiceHealthCheck" /> without the shared host: real
///     listeners on OS-assigned UDP ports, one of them blocked by another socket, and the hosted
///     service that starts it at boot. An enabled service that could not bind must make the
///     check Unhealthy with the operating system's reason next to the service's name, and the
///     hosted service must not throw (an exception there stops the process).
/// </summary>
public sealed class DhcpServiceHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WithADhcpv4PortThatIsTaken_IsUnhealthyAndNamesTheReason()
    {
        using Socket occupant = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        occupant.Bind(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)occupant.LocalEndPoint).Port;

        Dhcpv4ProtocolOptions v4 = new() { Enable = true, ServerPort = port };
        // The handler is never used: the bind fails before the first receive.
        Dhcpv4UdpListener listener = new(
            null,
            MsOptions.Create(v4),
            NullLogger<Dhcpv4UdpListener>.Instance
        );
        await new Dhcpv4ListenerHostedService(
            listener,
            MsOptions.Create(v4),
            NullLogger<Dhcpv4ListenerHostedService>.Instance
        ).StartAsync(CancellationToken.None);

        DhcpServiceHealthCheck check = CreateCheck(listener, v4);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("DHCPv4 (", result.Description);
        Assert.Contains("in use", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_WithAServiceStoppedWithoutABindFailure_NamesTheServiceAlone()
    {
        Dhcpv4ProtocolOptions v4 = new() { Enable = true, ServerPort = 0 };
        Dhcpv4UdpListener listener = new(
            null,
            MsOptions.Create(v4),
            NullLogger<Dhcpv4UdpListener>.Instance
        );

        DhcpServiceHealthCheck check = CreateCheck(listener, v4);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("DHCPv4.", result.Description);
        Assert.DoesNotContain("DHCPv4 (", result.Description);
    }

    /// <summary>
    ///     This method builds the check over one DHCPv4 listener; the other three services are
    ///     disabled, so the check leaves them out.
    /// </summary>
    private static DhcpServiceHealthCheck CreateCheck(
        Dhcpv4UdpListener listener,
        Dhcpv4ProtocolOptions v4
    ) =>
        new(
            [listener],
            MsOptions.Create(v4),
            MsOptions.Create(new Dhcpv6ProtocolOptions { Enable = false }),
            MsOptions.Create(new RaProtocolOptions { Enable = false }),
            MsOptions.Create(new TftpServerOptions { Enable = false })
        );
}
