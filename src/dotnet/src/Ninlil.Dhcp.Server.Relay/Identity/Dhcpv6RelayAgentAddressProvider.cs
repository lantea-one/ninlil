using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Identity;

/// <summary>
///     This class resolves the relay's RFC 8415 "link-address" once, when the singleton is
///     created (see <see cref="Extension.ServiceCollectionDhcpv6RelayExtension.AddNinlilDhcpv6Relay" />).
///     It uses <see cref="Dhcpv6RelayOptions.RelayLinkAddress" /> when that is a valid
///     non-link-local IPv6 address, and otherwise the first IPv6 address that is neither loopback
///     nor link-local on any interface that is up. Link-local addresses are excluded because the
///     server uses "link-address" to identify the client's link, and a link-local address cannot
///     do that.
///     <para>
///         As with <see cref="Dhcpv4RelayAgentAddressProvider" />, the detection logic repeats
///         <c>Ninlil.Dhcp.Server.Core.Identity.Dhcpv6ServerIdentifierProvider</c> to keep the
///         relay and the local engine independent.
///     </para>
/// </summary>
/// <param name="options">The relay configuration.</param>
/// <param name="logger">The logger the chosen address and any fallback are recorded to.</param>
public sealed class Dhcpv6RelayAgentAddressProvider(
    IOptions<Dhcpv6RelayOptions> options,
    ILogger<Dhcpv6RelayAgentAddressProvider> logger
) : IDhcpv6RelayAgentAddressProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = Resolve(options.Value, logger);

    /// <summary>
    ///     This method chooses the address as described in the class summary.
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
            // Skip interfaces that are down and loopback interfaces; neither faces clients.
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

                // A link-local or loopback address cannot identify the client's link.
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

        // Fall back to loopback instead of throwing, as Dhcpv4RelayAgentAddressProvider does.
        logger.LogWarning(
            "No non-loopback, non-link-local IPv6 network interface was found; this relay's own link-address defaults to loopback, which is almost certainly wrong for a real deployment. Set {OptionName} explicitly.",
            $"{Dhcpv6RelayOptions.SectionName}:{nameof(Dhcpv6RelayOptions.RelayLinkAddress)}"
        );
        return IPAddress.IPv6Loopback;
    }
}
