using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Identity;

/// <summary>
///     This class resolves the relay's <c>giaddr</c> address once, when the singleton is created
///     (see <see cref="Extension.ServiceCollectionDhcpv4RelayExtension.AddNinlilDhcpv4Relay" />).
///     It uses <see cref="Dhcpv4RelayOptions.RelayAgentAddress" /> when that is a valid IPv4
///     address, and otherwise the first non-loopback IPv4 address on any interface that is up.
///     <para>
///         The detection logic repeats
///         <c>Ninlil.Dhcp.Server.Core.Identity.DhcpServerIdentifierProvider</c> instead of
///         referencing that project. The relay and the local engine are alternatives that never
///         run together (see <see cref="Dhcpv4RelayOptions" />), so a short copy keeps the two
///         projects independent.
///     </para>
/// </summary>
/// <param name="options">The relay configuration.</param>
/// <param name="logger">The logger the chosen address and any fallback are recorded to.</param>
public sealed class Dhcpv4RelayAgentAddressProvider(
    IOptions<Dhcpv4RelayOptions> options,
    ILogger<Dhcpv4RelayAgentAddressProvider> logger
) : IDhcpv4RelayAgentAddressProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = Resolve(options.Value, logger);

    /// <summary>
    ///     This method chooses the address as described in the class summary.
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

        // Fall back to loopback instead of throwing. A disabled relay never uses the value, and
        // an enabled relay with no usable interface cannot work whatever is returned here.
        logger.LogWarning(
            "No non-loopback IPv4 network interface was found; this relay's own giaddr address defaults to loopback, which is almost certainly wrong for a real deployment. Set {OptionName} explicitly.",
            $"{Dhcpv4RelayOptions.SectionName}:{nameof(Dhcpv4RelayOptions.RelayAgentAddress)}"
        );
        return IPAddress.Loopback;
    }
}
