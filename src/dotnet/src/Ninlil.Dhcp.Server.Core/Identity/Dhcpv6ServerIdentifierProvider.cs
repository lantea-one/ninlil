using System.Buffers.Binary;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Identity;

/// <summary>
///     This class resolves this server's own DHCPv6 DUID exactly once, at construction time
///     (registered as a singleton -- see
///     <see cref="Extension.ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpv6ServerCore" />),
///     either from <see cref="DhcpServerOptions.ServerDuid" /> when the operator set one
///     explicitly, or by deriving a DUID-LL (RFC 8415 Section 11.2) from the link-layer address of
///     the first non-loopback, operational network interface found -- see
///     <see cref="DhcpServerOptions.ServerDuid" />'s own doc comment for why that needs no
///     persistence to stay stable, unlike a randomly-generated DUID would.
/// </summary>
/// <param name="options">This server's own core configuration.</param>
/// <param name="logger">The logger this resolution's own outcome is recorded to.</param>
public sealed class Dhcpv6ServerIdentifierProvider(
    IOptions<DhcpServerOptions> options,
    ILogger<Dhcpv6ServerIdentifierProvider> logger
) : IDhcpv6ServerIdentifierProvider
{
    /// <summary>
    ///     RFC 8415 Section 11.2's DUID-LL type code.
    /// </summary>
    private const ushort DuidTypeLinkLayer = 3;

    /// <summary>
    ///     The IANA "ARP Hardware Types" registry value for Ethernet, the overwhelmingly common
    ///     case for any real network interface this method would ever pick.
    /// </summary>
    private const ushort HardwareTypeEthernet = 1;

    /// <inheritdoc />
    public byte[] Value { get; } = Resolve(options.Value, logger);

    /// <summary>
    ///     This method performs the actual one-time resolution described in this class's own doc
    ///     comment.
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

        // No usable interface was found at all -- fall back to a fixed, obviously-placeholder
        // DUID rather than throwing, the same "a listener that never binds never actually needs
        // this value" reasoning DhcpServerIdentifierProvider's own IPv4 fallback already
        // documents.
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
    ///     This method attempts to decode an operator-supplied DUID string, tolerating an
    ///     optional colon separator between byte pairs the same way
    ///     <c>Dhcpv4ClientIdentifier</c>'s own string handling does.
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
