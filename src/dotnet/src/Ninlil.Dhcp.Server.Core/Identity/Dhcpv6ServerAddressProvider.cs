using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This class resolves the server's IPv6 address once, when it is constructed (it is a
///     singleton; see
///     <see cref="Extension.ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpv6ServerCore" />).
///     It uses <see cref="DhcpServerOptions.ServerIPv6Address" /> when that is a valid
///     non-link-local IPv6 address, and otherwise the first IPv6 address that is neither loopback
///     nor link-local on an interface that is up and not loopback. With none it falls back to
///     <c>::1</c> and logs a warning. The address is what
///     <see cref="Ninlil.Dhcp.Common.Enum.DhcpOptionValueSourceEnum.ServerAddress" /> options
///     carry (see <see cref="Options.Dhcpv6OptionOverlayService" />), unlike the DUID from
///     <see cref="IDhcpv6ServerIdentifierProvider" />.
///     <para>
///         The detection repeats the logic of
///         <c>Ninlil.Dhcp.Server.Relay.Identity.Dhcpv6RelayAgentAddressProvider</c> instead of
///         referencing that project, since the local engine and the relay are alternatives that
///         are never registered together.
///     </para>
/// </summary>
/// <param name="options">The server engine configuration.</param>
/// <param name="logger">The logger the result is recorded to.</param>
public sealed class Dhcpv6ServerAddressProvider(
    IOptions<DhcpServerOptions> options,
    ILogger<Dhcpv6ServerAddressProvider> logger
) : IDhcpv6ServerAddressProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = Resolve(options.Value, logger);

    /// <summary>
    ///     This method performs the one-time resolution described in the class summary.
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
            // Clients can only be reached through an interface that is up, and loopback is
            // never a client-facing link.
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

                // A link-local address (fe80::/10) only works on one interface, so it is a poor
                // value for an option such as a DNS server. Look for a global or ULA address, as
                // Dhcpv6RelayAgentAddressProvider does for the RFC 8415 "link-address".
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

        // No usable interface. Fall back to loopback rather than throw, as
        // DhcpServerIdentifierProvider does; a ServerAddress option then carries a useless
        // address, but the missing interface is the real problem.
        logger.LogWarning(
            "No non-loopback, non-link-local IPv6 network interface was found; this server's own DHCPv6 server address defaults to loopback, which is almost certainly wrong for a real deployment. Set {OptionName} explicitly.",
            $"{DhcpServerOptions.SectionName}:{nameof(DhcpServerOptions.ServerIPv6Address)}"
        );
        return IPAddress.IPv6Loopback;
    }
}
