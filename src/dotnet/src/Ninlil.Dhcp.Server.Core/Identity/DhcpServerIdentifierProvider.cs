using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This class resolves the DHCPv4 Server Identifier address once, when it is constructed
///     (it is a singleton; see
///     <see cref="Extension.ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpServerCore" />).
///     It uses <see cref="DhcpServerOptions.ServerIdentifierAddress" /> when that is a valid IPv4
///     address, and otherwise the first IPv4 address of the first interface that is up and not
///     loopback. Link-local (169.254.0.0/16) and tunnel addresses are not skipped. With no such
///     interface it falls back to loopback and logs a warning.
/// </summary>
/// <param name="options">The server engine configuration.</param>
/// <param name="logger">The logger the result is recorded to.</param>
public sealed class DhcpServerIdentifierProvider(
    IOptions<DhcpServerOptions> options,
    ILogger<DhcpServerIdentifierProvider> logger
) : IDhcpServerIdentifierProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = Resolve(options.Value, logger);

    /// <summary>
    ///     This method performs the one-time resolution described in the class summary.
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

        // No usable interface (a container without networking, for example). Fall back to
        // loopback rather than throw: a disabled listener never uses the value, and an enabled
        // one without an interface has a problem this value cannot fix.
        logger.LogWarning(
            "No non-loopback IPv4 network interface was found; the DHCPv4 Server Identifier address defaults to loopback, which is almost certainly wrong for a real deployment. Set {OptionName} explicitly.",
            $"{DhcpServerOptions.SectionName}:{nameof(DhcpServerOptions.ServerIdentifierAddress)}"
        );
        return IPAddress.Loopback;
    }
}
