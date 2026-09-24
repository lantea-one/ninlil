using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Identity;

/// <summary>
///     This class resolves this relay's own <c>giaddr</c> address exactly once, at construction
///     time (this class is registered as a singleton -- see
///     <see cref="Extension.ServiceCollectionDhcpv4RelayExtension.AddNinlilDhcpv4Relay" />), either
///     from <see cref="Dhcpv4RelayOptions.RelayAgentAddress" /> when the operator set one
///     explicitly, or by picking the first non-loopback, operational IPv4 unicast address bound to
///     any network interface on this host.
///     <para>
///         This deliberately duplicates
///         <c>Ninlil.Dhcp.Server.Core.Identity.DhcpServerIdentifierProvider</c>'s own small
///         auto-detection logic rather than depending on that project: <c>Server.Core</c> and
///         <c>Server.Relay</c> are two mutually exclusive, alternative
///         <c>IDhcpv4RequestHandler</c> implementations (see <see cref="Dhcpv4RelayOptions" />'s
///         own doc comment), and a project reference between two engines that are never both
///         active at once would be a genuinely misleading dependency to carry -- a ~20-line,
///         easily-verified duplication is the honest tradeoff for keeping them independent.
///     </para>
/// </summary>
/// <param name="options">This relay's own configuration.</param>
/// <param name="logger">The logger this resolution's own outcome is recorded to.</param>
public sealed class Dhcpv4RelayAgentAddressProvider(
    IOptions<Dhcpv4RelayOptions> options,
    ILogger<Dhcpv4RelayAgentAddressProvider> logger
) : IDhcpv4RelayAgentAddressProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = Resolve(options.Value, logger);

    /// <summary>
    ///     This method performs the actual one-time resolution described in this class's own doc
    ///     comment.
    /// </summary>
    private static IPAddress Resolve(
        Dhcpv4RelayOptions options,
        ILogger<Dhcpv4RelayAgentAddressProvider> logger
    )
    {
        if (!string.IsNullOrWhiteSpace(options.RelayAgentAddress))
        {
            if (
                IPAddress.TryParse(options.RelayAgentAddress, out IPAddress configured)
                && configured.AddressFamily == AddressFamily.InterNetwork
            )
                return configured;

            logger.LogWarning(
                "{OptionName} ({Value}) is not a valid IPv4 address; falling back to auto-detection.",
                $"{Dhcpv4RelayOptions.SectionName}:{nameof(Dhcpv4RelayOptions.RelayAgentAddress)}",
                options.RelayAgentAddress
            );
        }

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Only a currently-up interface can plausibly be the one this relay is actually
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
                        "Auto-detected {Address} on interface {InterfaceName} as this relay's own giaddr address.",
                        candidate,
                        networkInterface.Name
                    );
                    return candidate;
                }
            }
        }

        // No usable interface was found at all -- fall back to loopback rather than throwing,
        // since a relay that never binds (Dhcpv4RelayOptions.Enable left false) never actually
        // needs this value, and a relay that is enabled but genuinely has no reachable interface
        // has a real deployment problem this value alone cannot fix either way.
        logger.LogWarning(
            "No non-loopback IPv4 network interface was found; this relay's own giaddr address defaults to loopback, which is almost certainly wrong for a real deployment. Set {OptionName} explicitly.",
            $"{Dhcpv4RelayOptions.SectionName}:{nameof(Dhcpv4RelayOptions.RelayAgentAddress)}"
        );
        return IPAddress.Loopback;
    }
}
