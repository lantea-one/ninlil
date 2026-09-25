using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This record represents the decoded contents of an EDNS Client Subnet option (RFC 7871): the
///     address family, the address (truncated to <see cref="SourcePrefixLength" /> bits and
///     zero-padded), how much of it the sender discloses, and how much of it the responding
///     server used for its answer.
/// </summary>
/// <param name="Family">The address family of <see cref="Address" />.</param>
/// <param name="Address">
///     The client subnet address, masked to <see cref="SourcePrefixLength" /> bits. See
///     <see cref="EdnsClientSubnetCodec.Encode" /> for why masking happens when encoding.
/// </param>
/// <param name="SourcePrefixLength">
///     How many leading bits of <see cref="Address" /> the sender discloses: the SOURCE
///     PREFIX-LENGTH field of RFC 7871 §6, set by the sender (usually a resolver). Zero means the
///     sender supports ECS but declines to disclose a subnet, which RFC 7871 §7.1.2 allows.
/// </param>
/// <param name="ScopePrefixLength">
///     How many leading bits of <see cref="Address" /> the responding server used for its answer:
///     the SCOPE PREFIX-LENGTH field of RFC 7871 §6. It is zero on a query. On a response it tells
///     the querier how much of the address mattered, so the answer can be cached for that whole
///     scope rather than only the exact address.
/// </param>
public sealed record EdnsClientSubnetOptionData(
    AddressFamily Family,
    IPAddress Address,
    byte SourcePrefixLength,
    byte ScopePrefixLength
);

/// <summary>
///     This class reads and writes the payload of an EDNS Client Subnet option (RFC 7871 §6): the
///     data of a <see cref="DnsEdnsOption" /> whose code is
///     <see cref="Ninlil.Dns.Common.Enum.EdnsOptionCodeEnum.ClientSubnet" />. Most EDNS options
///     travel as opaque bytes and are only parsed where they are used (Cookies, for example, in
///     <c>Ninlil.Dns.Server.Core.Cookies.DnsCookieService</c>). ECS has a shared codec here because
///     its payload is both read from client queries and built for outbound upstream queries
///     (<c>Ninlil.Dns.Server.Core.ClientSubnet.EdnsClientSubnetService</c>).
/// </summary>
public static class EdnsClientSubnetCodec
{
    /// <summary>This constant contains RFC 7871 §6's assigned family value for IPv4.</summary>
    private const ushort FamilyIPv4 = 1;

    /// <summary>This constant contains RFC 7871 §6's assigned family value for IPv6.</summary>
    private const ushort FamilyIPv6 = 2;

    /// <summary>
    ///     This method builds the payload of an outbound ECS option: a 2-byte family, the source and
    ///     scope prefix lengths, and the address truncated to <c>ceil(sourcePrefixLength / 8)</c>
    ///     bytes. RFC 7871 §6 requires that length and requires bits beyond
    ///     <paramref name="sourcePrefixLength" /> in the last byte to be zero, so masking happens here
    ///     rather than in every caller.
    /// </summary>
    /// <param name="address">The (unmasked) client subnet address to encode.</param>
    /// <param name="sourcePrefixLength">How many leading bits of <paramref name="address" /> to disclose.</param>
    /// <param name="scopePrefixLength">
    ///     How many leading bits a response used; always <c>0</c> on a query.
    /// </param>
    /// <returns>The encoded <see cref="DnsEdnsOption" />, ready to add to a message's EDNS options.</returns>
    public static DnsEdnsOption Encode(
        IPAddress address,
        byte sourcePrefixLength,
        byte scopePrefixLength
    )
    {
        ushort family = address.AddressFamily switch
        {
            AddressFamily.InterNetwork => FamilyIPv4,
            AddressFamily.InterNetworkV6 => FamilyIPv6,
            _ => throw new ArgumentException(
                $"'{address}' is neither an IPv4 nor an IPv6 address.",
                nameof(address)
            ),
        };

        byte[] fullAddressBytes = address.GetAddressBytes();
        int addressByteCount = (sourcePrefixLength + 7) / 8;
        byte[] truncatedAddressBytes = new byte[addressByteCount];
        Array.Copy(fullAddressBytes, truncatedAddressBytes, addressByteCount);

        // Zero the bits beyond sourcePrefixLength in the last byte (RFC 7871 §6), so two
        // addresses in the same /21 encode identically.
        int remainderBits = sourcePrefixLength % 8;

        if (remainderBits != 0 && addressByteCount > 0)
        {
            byte mask = (byte)(0xFF << (8 - remainderBits));
            truncatedAddressBytes[^1] &= mask;
        }

        byte[] payload = new byte[4 + addressByteCount];
        payload[0] = (byte)(family >> 8);
        payload[1] = (byte)family;
        payload[2] = sourcePrefixLength;
        payload[3] = scopePrefixLength;
        Array.Copy(truncatedAddressBytes, 0, payload, 4, addressByteCount);

        return new DnsEdnsOption((ushort)EdnsOptionCodeEnum.ClientSubnet, payload);
    }

    /// <summary>
    ///     This method decodes an ECS option's payload into <see cref="EdnsClientSubnetOptionData" />.
    /// </summary>
    /// <param name="option">
    ///     The option to decode. This method does not check <see cref="DnsEdnsOption.Code" />; the
    ///     caller selects the <see cref="Ninlil.Dns.Common.Enum.EdnsOptionCodeEnum.ClientSubnet" />
    ///     option.
    /// </param>
    /// <param name="data">The decoded contents, if decoding succeeded.</param>
    /// <returns>
    ///     A <see langword="bool" /> indicating whether the payload was a well-formed ECS option. It
    ///     is <see langword="false" /> for a truncated payload, an unknown family, or an address
    ///     longer than its family allows. The caller then ignores the option, as with an unknown
    ///     option code, rather than failing the whole message.
    /// </returns>
    public static bool TryDecode(DnsEdnsOption option, out EdnsClientSubnetOptionData data)
    {
        data = null;
        byte[] payload = option?.Data;

        // The 2-byte family and the two 1-byte prefix lengths must be present.
        if (payload is null || payload.Length < 4)
            return false;

        ushort family = (ushort)((payload[0] << 8) | payload[1]);
        byte sourcePrefixLength = payload[2];
        byte scopePrefixLength = payload[3];

        // Map the wire family to its AddressFamily and full bit width. An unknown family maps
        // to AddressFamily.Unknown so the next check rejects it.
        (AddressFamily addressFamily, int maxPrefixLength) = family switch
        {
            FamilyIPv4 => (AddressFamily.InterNetwork, 32),
            FamilyIPv6 => (AddressFamily.InterNetworkV6, 128),
            _ => (AddressFamily.Unknown, 0),
        };

        if (addressFamily == AddressFamily.Unknown || sourcePrefixLength > maxPrefixLength)
            return false;

        // The payload must carry exactly the address bytes sourcePrefixLength implies: fewer
        // is truncated, more is padding RFC 7871 §6 does not allow.
        int expectedAddressByteCount = (sourcePrefixLength + 7) / 8;

        if (payload.Length != 4 + expectedAddressByteCount)
            return false;

        // IPAddress needs a full 4- or 16-byte array, so copy the disclosed bytes into a
        // zero-filled buffer.
        byte[] fullAddressBytes = new byte[maxPrefixLength / 8];
        Array.Copy(payload, 4, fullAddressBytes, 0, expectedAddressByteCount);

        data = new EdnsClientSubnetOptionData(
            addressFamily,
            new IPAddress(fullAddressBytes),
            sourcePrefixLength,
            scopePrefixLength
        );
        return true;
    }
}
