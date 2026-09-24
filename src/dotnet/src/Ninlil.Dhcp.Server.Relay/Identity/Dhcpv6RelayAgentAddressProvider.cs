using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Identity;

/// <summary>
///     This class resolves this relay's own RFC 8415 Section 7.1 "link-address" exactly once, at
///     construction time (this class is registered as a singleton -- see
///     <see cref="Extension.ServiceCollectionDhcpv6RelayExtension.AddNinlilDhcpv6Relay" />), either
///     from <see cref="Dhcpv6RelayOptions.RelayLinkAddress" /> when the operator set one
///     explicitly, or by picking the first non-loopback, non-link-local, operational IPv6 unicast
///     address bound to any network interface on this host -- the DHCPv6 analogue of
///     <see cref="Dhcpv4RelayAgentAddressProvider" />, differing only in also excluding a
///     link-local candidate, since RFC 8415 Section 7.1 explicitly requires "link-address" to be a
///     global address a server can use to identify a real, specific link.
///     <para>
///         This deliberately duplicates <c>Ninlil.Dhcp.Server.Core.Identity.Dhcpv6ServerIdentifierProvider</c>'s
///         own small auto-detection logic rather than depending on that project, the exact same
///         "two mutually exclusive, alternative engines, never both active at once" tradeoff
///         <see cref="Dhcpv4RelayAgentAddressProvider" />'s own doc comment already documents.
///     </para>
/// </summary>
/// <param name="options">This relay's own configuration.</param>
/// <param name="logger">The logger this resolution's own outcome is recorded to.</param>
public sealed class Dhcpv6RelayAgentAddressProvider(
    IOptions<Dhcpv6RelayOptions> options,
    ILogger<Dhcpv6RelayAgentAddressProvider> logger
) : IDhcpv6RelayAgentAddressProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = Resolve(options.Value, logger);

    /// <summary>
    ///     This method performs the actual one-time resolution described in this class's own doc
    ///     comment.
    /// </summary>
    private static IPAddress Resolve(
        Dhcpv6RelayOptions options,
        ILogger<Dhcpv6RelayAgentAddressProvider> logger
    )
    {
        if (!string.IsNullOrWhiteSpace(options.RelayLinkAddress))
        {
            if (
                IPAddress.TryParse(options.RelayLinkAddress, out IPAddress configured)
                && configured.AddressFamily == AddressFamily.InterNetworkV6
                && !configured.IsIPv6LinkLocal
            )
                return configured;

            logger.LogWarning(
                "{OptionName} ({Value}) is not a valid, non-link-local IPv6 address; falling back to auto-detection.",
                $"{Dhcpv6RelayOptions.SectionName}:{nameof(Dhcpv6RelayOptions.RelayLinkAddress)}",
                options.RelayLinkAddress
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

                // RFC 8415 Section 7.1 requires "link-address" to be a global address -- a
                // link-local or loopback address cannot identify a specific, real link the way
                // this field needs to.
                if (
                    candidate.AddressFamily == AddressFamily.InterNetworkV6
                    && !candidate.IsIPv6LinkLocal
                    && !IPAddress.IsLoopback(candidate)
                )
                {
                    logger.LogInformation(
                        "Auto-detected {Address} on interface {InterfaceName} as this relay's own link-address.",
                        candidate,
                        networkInterface.Name
                    );
                    return candidate;
                }
            }
        }

        // No usable interface was found at all -- fall back to loopback rather than throwing, the
        // same "a relay that never binds never actually needs this value" reasoning
        // Dhcpv4RelayAgentAddressProvider's own doc comment already documents.
        logger.LogWarning(
            "No non-loopback, non-link-local IPv6 network interface was found; this relay's own link-address defaults to loopback, which is almost certainly wrong for a real deployment. Set {OptionName} explicitly.",
            $"{Dhcpv6RelayOptions.SectionName}:{nameof(Dhcpv6RelayOptions.RelayLinkAddress)}"
        );
        return IPAddress.IPv6Loopback;
    }
}
