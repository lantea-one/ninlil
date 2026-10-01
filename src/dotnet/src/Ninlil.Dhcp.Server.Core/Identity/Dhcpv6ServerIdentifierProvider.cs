using System.Buffers.Binary;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This class resolves the server's DHCPv6 DUID once, when it is constructed (it is a
///     singleton; see
///     <see cref="Extension.ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpv6ServerCore" />).
///     It uses <see cref="DhcpServerOptions.ServerDuid" /> when that is valid hex, and otherwise
///     builds a DUID-LL (RFC 8415 Section 11.4) from the link-layer address of the first
///     interface that is up, not loopback and has one. A DUID-LL needs no storage to stay stable
///     (see <see cref="DhcpServerOptions.ServerDuid" />). With no such interface it falls back to
///     a fixed placeholder DUID and logs a warning.
/// </summary>
/// <param name="options">The server engine configuration.</param>
/// <param name="logger">The logger the result is recorded to.</param>
public sealed class Dhcpv6ServerIdentifierProvider(
    IOptions<DhcpServerOptions> options,
    ILogger<Dhcpv6ServerIdentifierProvider> logger
) : IDhcpv6ServerIdentifierProvider
{
    /// <summary>
    ///     The DUID-LL type code (RFC 8415 Section 11.4).
    /// </summary>
    private const ushort DuidTypeLinkLayer = 3;

    /// <summary>
    ///     The IANA "ARP Hardware Types" value for Ethernet. It is used for whatever interface is
    ///     picked, which is almost always Ethernet or Wi-Fi.
    /// </summary>
    private const ushort HardwareTypeEthernet = 1;

    /// <inheritdoc />
    public byte[] Value { get; } = Resolve(options.Value, logger);

    /// <summary>
    ///     This method performs the one-time resolution described in the class summary.
    /// </summary>
    private static byte[] Resolve(
        DhcpServerOptions options,
        ILogger<Dhcpv6ServerIdentifierProvider> logger
    )
    {
        if (!string.IsNullOrWhiteSpace(options.ServerDuid))
        {
            byte[] configured = TryParseHex(options.ServerDuid);

            if (configured is { Length: > 0 })
                return configured;

            logger.LogWarning(
                "{OptionName} ({Value}) is not a valid hexadecimal DUID; falling back to auto-detection.",
                $"{DhcpServerOptions.SectionName}:{nameof(DhcpServerOptions.ServerDuid)}",
                options.ServerDuid
            );
        }

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (
                networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback
            )
                continue;

            byte[] linkLayerAddress = networkInterface.GetPhysicalAddress().GetAddressBytes();

            if (linkLayerAddress is not { Length: > 0 })
                continue;

            byte[] duid = new byte[4 + linkLayerAddress.Length];
            BinaryPrimitives.WriteUInt16BigEndian(duid, DuidTypeLinkLayer);
            BinaryPrimitives.WriteUInt16BigEndian(duid.AsSpan(2), HardwareTypeEthernet);
            linkLayerAddress.CopyTo(duid, 4);

            logger.LogInformation(
                "Auto-detected a DUID-LL from interface {InterfaceName}'s own link-layer address as this server's own DHCPv6 Server Identifier.",
                networkInterface.Name
            );
            return duid;
        }

        // No usable interface. Fall back to a fixed placeholder DUID rather than throw, for the
        // reason given in DhcpServerIdentifierProvider.
        logger.LogWarning(
            "No non-loopback network interface with a link-layer address was found; the DHCPv6 Server Identifier defaults to a fixed placeholder DUID, which is almost certainly wrong for a real deployment. Set {OptionName} explicitly.",
            $"{DhcpServerOptions.SectionName}:{nameof(DhcpServerOptions.ServerDuid)}"
        );

        byte[] placeholder = new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(placeholder, DuidTypeLinkLayer);
        BinaryPrimitives.WriteUInt16BigEndian(placeholder.AsSpan(2), HardwareTypeEthernet);
        return placeholder;
    }

    /// <summary>
    ///     This method decodes a configured DUID string, ignoring every non-hex character (so any
    ///     separator works), or returns <see langword="null" /> for no digits or an odd number.
    /// </summary>
    private static byte[] TryParseHex(string value)
    {
        string cleaned = new([.. value.Where(Uri.IsHexDigit)]);

        if (cleaned.Length == 0 || cleaned.Length % 2 != 0)
            return null;

        byte[] result = new byte[cleaned.Length / 2];

        for (int i = 0; i < result.Length; i++)
        {
            if (
                !byte.TryParse(
                    cleaned.AsSpan(i * 2, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out result[i]
                )
            )
                return null;
        }

        return result;
    }
}
