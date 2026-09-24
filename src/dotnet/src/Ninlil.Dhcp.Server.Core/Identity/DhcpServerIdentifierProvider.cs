using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This class resolves this server's own Server Identifier address exactly once, at
///     construction time (this class is registered as a singleton -- see
///     <see cref="Extension.ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpServerCore" />),
///     either from <see cref="DhcpServerOptions.ServerIdentifierAddress" /> when the operator set
///     one explicitly, or by picking the first non-loopback, non-link-local, operational IPv4
///     unicast address bound to any network interface on this host.
/// </summary>
/// <param name="options">This server's own core configuration.</param>
/// <param name="logger">The logger this resolution's own outcome is recorded to.</param>
public sealed class DhcpServerIdentifierProvider(
    IOptions<DhcpServerOptions> options,
    ILogger<DhcpServerIdentifierProvider> logger
) : IDhcpServerIdentifierProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = Resolve(options.Value, logger);

    /// <summary>
    ///     This method performs the actual one-time resolution described in this class's own doc
    ///     comment.
    /// </summary>
    private static IPAddress Resolve(
        DhcpServerOptions options,
        ILogger<DhcpServerIdentifierProvider> logger
    )
    {
        if (!string.IsNullOrWhiteSpace(options.ServerIdentifierAddress))
        {
            if (
                IPAddress.TryParse(options.ServerIdentifierAddress, out IPAddress configured)
                && configured.AddressFamily == AddressFamily.InterNetwork
            )
                return configured;

            logger.LogWarning(
                "{OptionName} ({Value}) is not a valid IPv4 address; falling back to auto-detection.",
                $"{DhcpServerOptions.SectionName}:{nameof(DhcpServerOptions.ServerIdentifierAddress)}",
                options.ServerIdentifierAddress
            );
        }

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Only a currently-up interface can plausibly be the one a DHCP client is actually
            // reachable through, and the loopback/tunnel interfaces this loop otherwise walks
            // right past never represent a real, client-facing LAN link.
            if (
                networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback
            )
                continue;

            foreach (
                UnicastIPAddressInformation unicastAddress in networkInterface
                    .GetIPProperties()
                    .UnicastAddresses
            )
            {
                IPAddress candidate = unicastAddress.Address;

                if (
                    candidate.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(candidate)
                )
                {
                    logger.LogInformation(
                        "Auto-detected {Address} on interface {InterfaceName} as this server's own DHCPv4 Server Identifier address.",
                        candidate,
                        networkInterface.Name
                    );
                    return candidate;
                }
            }
        }

        // No usable interface was found at all (a container with no real network attached, for
        // example) -- fall back to loopback rather than throwing, since a listener that never
        // binds (Dhcpv4ProtocolOptions.Enable left false) never actually needs this value, and a
        // listener that is enabled but genuinely has no reachable interface has a real deployment
        // problem this value alone cannot fix either way.
        logger.LogWarning(
            "No non-loopback IPv4 network interface was found; the DHCPv4 Server Identifier address defaults to loopback, which is almost certainly wrong for a real deployment. Set {OptionName} explicitly.",
            $"{DhcpServerOptions.SectionName}:{nameof(DhcpServerOptions.ServerIdentifierAddress)}"
        );
        return IPAddress.Loopback;
    }
}
