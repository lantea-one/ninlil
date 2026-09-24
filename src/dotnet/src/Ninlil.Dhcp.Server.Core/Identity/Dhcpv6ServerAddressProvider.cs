using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This class resolves this server's own real IPv6 address exactly once, at construction time
///     (this class is registered as a singleton -- see
///     <see cref="Extension.ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpv6ServerCore" />),
///     either from <see cref="DhcpServerOptions.ServerIPv6Address" /> when the operator set one
///     explicitly, or by picking the first non-loopback, non-link-local, operational IPv6 unicast
///     address bound to any network interface on this host.
///     <para>
///         This deliberately duplicates
///         <c>Ninlil.Dhcp.Server.Relay.Identity.Dhcpv6RelayAgentAddressProvider</c>'s own
///         auto-detection logic byte-for-byte rather than depending on that project -- the exact
///         same "two mutually exclusive, alternative engines, never both active at once" tradeoff
///         that class's own doc comment already documents, applied here across the local-engine/
///         relay-engine boundary rather than the DHCPv4/DHCPv6 one. Unlike
///         <see cref="IDhcpv6ServerIdentifierProvider" /> (this server's own DUID, an opaque
///         protocol identifier with no dialable-address meaning at all), this provider exists
///         specifically to answer "what is this server's own real IPv6 address," the value a
///         configured DHCPv6 option's <see cref="Ninlil.Dhcp.Common.Enum.DhcpOptionValueSourceEnum.ServerAddress" />
///         value source substitutes at delivery time (see
///         <see cref="Options.Dhcpv6OptionOverlayService" />).
///     </para>
/// </summary>
/// <param name="options">This server's own core configuration.</param>
/// <param name="logger">The logger this resolution's own outcome is recorded to.</param>
public sealed class Dhcpv6ServerAddressProvider(
    IOptions<DhcpServerOptions> options,
    ILogger<Dhcpv6ServerAddressProvider> logger
) : IDhcpv6ServerAddressProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = Resolve(options.Value, logger);

    /// <summary>
    ///     This method performs the actual one-time resolution described in this class's own doc
    ///     comment.
    /// </summary>
    private static IPAddress Resolve(
        DhcpServerOptions options,
        ILogger<Dhcpv6ServerAddressProvider> logger
    )
    {
        if (!string.IsNullOrWhiteSpace(options.ServerIPv6Address))
        {
            if (
                IPAddress.TryParse(options.ServerIPv6Address, out IPAddress configured)
                && configured.AddressFamily == AddressFamily.InterNetworkV6
                && !configured.IsIPv6LinkLocal
            )
                return configured;

            logger.LogWarning(
                "{OptionName} ({Value}) is not a valid, non-link-local IPv6 address; falling back to auto-detection.",
                $"{DhcpServerOptions.SectionName}:{nameof(DhcpServerOptions.ServerIPv6Address)}",
                options.ServerIPv6Address
            );
        }

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Only a currently-up interface can plausibly be the one a DHCPv6 client is actually
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

                // A link-local address (fe80::/10) is only ever valid scoped to one specific
                // interface, and is almost never the address an operator actually wants handed to
                // a client as "this server's own address" for a general-purpose option such as a
                // DNS server -- a real, globally-routable (or at least ULA/site-local) address is
                // what this provider looks for, the same requirement
                // Dhcpv6RelayAgentAddressProvider's own doc comment documents for RFC 8415 Section
                // 7.1's "link-address" field.
                if (
                    candidate.AddressFamily == AddressFamily.InterNetworkV6
                    && !candidate.IsIPv6LinkLocal
                    && !IPAddress.IsLoopback(candidate)
                )
                {
                    logger.LogInformation(
                        "Auto-detected {Address} on interface {InterfaceName} as this server's own DHCPv6 server address.",
                        candidate,
                        networkInterface.Name
                    );
                    return candidate;
                }
            }
        }

        // No usable interface was found at all -- fall back to loopback rather than throwing, the
        // same "a listener that never binds never actually needs this value" reasoning
        // DhcpServerIdentifierProvider's own IPv4 fallback already documents. A ServerAddress
        // value source resolving to this fallback still delivers a genuinely wrong address to a
        // real client, but that is a real deployment problem (no usable IPv6 interface at all)
        // this value alone cannot fix either way.
        logger.LogWarning(
            "No non-loopback, non-link-local IPv6 network interface was found; this server's own DHCPv6 server address defaults to loopback, which is almost certainly wrong for a real deployment. Set {OptionName} explicitly.",
            $"{DhcpServerOptions.SectionName}:{nameof(DhcpServerOptions.ServerIPv6Address)}"
        );
        return IPAddress.IPv6Loopback;
    }
}
