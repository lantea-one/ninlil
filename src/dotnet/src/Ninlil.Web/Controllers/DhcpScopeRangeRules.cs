using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This class holds the placement rules <see cref="DhcpScopesController" /> applies to a
///     scope's address range, kept apart from the controller so they can be read and tested on
///     their own: the range lies inside one subnet of the scope's prefix length, an IPv4 range
///     does not include the subnet's network or broadcast address, and no other enabled scope of
///     the same IP version and kind uses an address of the range.
///     <para>
///         The first rule matters because the engine builds the subnet mask it sends to clients
///         from <c>subnetPrefixLength</c> alone and picks addresses from the range: a range that
///         leaves the subnet hands out addresses the client then treats as off-link. The second
///         network and broadcast rule matters because those addresses name the subnet and every
///         host in it, so a client given one cannot use it. The overlap rule matters because two scopes that both hand out an address would answer the same
///         client, and the scope resolvers choose between them by the client's identity or
///         relay address, not by range, so the same address could be leased twice.
///     </para>
/// </summary>
internal static class DhcpScopeRangeRules
{
    /// <summary>
    ///     This method checks that both ends of the range share the first
    ///     <paramref name="prefixLength" /> bits, so the range lies inside one subnet of that
    ///     size. Both addresses are already parsed and of one family.
    /// </summary>
    /// <param name="start">The first address of the range.</param>
    /// <param name="end">The last address of the range.</param>
    /// <param name="prefixLength">The subnet prefix length in bits.</param>
    /// <param name="error">The message for the 400 response, or <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the range lies inside one subnet.</returns>
    public static bool TryCheckInsideSubnet(
        string start,
        string end,
        int prefixLength,
        out string error
    )
    {
        error = null;
        byte[] startBytes = IPAddress.Parse(start).GetAddressBytes();
        byte[] endBytes = IPAddress.Parse(end).GetAddressBytes();

        for (int bit = 0; bit < prefixLength; bit++)
        {
            int mask = 0x80 >> (bit % 8);
            if ((startBytes[bit / 8] & mask) == (endBytes[bit / 8] & mask))
                continue;

            error =
                $"The range {start} to {end} does not lie inside one /{prefixLength} subnet. "
                + $"Both ends must share the first {prefixLength} bits of the address, or "
                + "'subnetPrefixLength' must be smaller.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method checks that an IPv4 range does not include the network address (all host
    ///     bits zero) or the broadcast address (all host bits one) of its subnet. A /31 and a
    ///     /32 have neither: RFC 3021 Section 2.1 says a 31-bit prefix on a point-to-point link
    ///     has two host addresses, and a /32 names one host, so every address of them may be
    ///     leased. An IPv6 range is never refused here, because DHCPv6 has no broadcast address.
    ///     The method expects the range to lie inside one subnet already
    ///     (<see cref="TryCheckInsideSubnet" />), so only its ends can be the edge addresses.
    /// </summary>
    /// <param name="start">The first address of the range.</param>
    /// <param name="end">The last address of the range.</param>
    /// <param name="prefixLength">The subnet prefix length in bits.</param>
    /// <param name="error">The message for the 400 response, or <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the range holds neither address.</returns>
    public static bool TryCheckNoNetworkOrBroadcast(
        string start,
        string end,
        int prefixLength,
        out string error
    )
    {
        error = null;
        IPAddress first = IPAddress.Parse(start);
        if (first.AddressFamily != AddressFamily.InterNetwork || prefixLength >= 31)
            return true;

        uint hostMask = uint.MaxValue >> prefixLength;
        uint startValue = BinaryPrimitives.ReadUInt32BigEndian(first.GetAddressBytes());
        uint endValue = BinaryPrimitives.ReadUInt32BigEndian(
            IPAddress.Parse(end).GetAddressBytes()
        );

        if ((startValue & hostMask) == 0)
        {
            error =
                $"The range starts on {start}, the network address of its /{prefixLength} subnet. "
                + "Start the range at the next address or later.";
            return false;
        }

        if ((endValue & hostMask) == hostMask)
        {
            error =
                $"The range ends on {end}, the broadcast address of its /{prefixLength} subnet. "
                + "End the range at the address before it or earlier.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method returns the message for the 400 response when the range shares an address
    ///     with <paramref name="other" />, or <see langword="null" /> when it does not. Both
    ///     scopes must be of one IP version, so the byte arrays have one length; the ranges are
    ///     compared as big-endian unsigned numbers.
    /// </summary>
    /// <param name="start">The first address of the range being written.</param>
    /// <param name="end">The last address of the range being written.</param>
    /// <param name="other">A stored scope of the same IP version and kind.</param>
    public static string DescribeOverlap(string start, string end, DhcpScopeEntity other)
    {
        byte[] startBytes = IPAddress.Parse(start).GetAddressBytes();
        byte[] endBytes = IPAddress.Parse(end).GetAddressBytes();
        byte[] otherStart = IPAddress.Parse(other.RangeStartAddress).GetAddressBytes();
        byte[] otherEnd = IPAddress.Parse(other.RangeEndAddress).GetAddressBytes();

        bool overlaps =
            startBytes.AsSpan().SequenceCompareTo(otherEnd) <= 0
            && otherStart.AsSpan().SequenceCompareTo(endBytes) <= 0;

        return overlaps
            ? $"The range {start} to {end} overlaps scope '{other.Name}' "
                + $"({other.RangeStartAddress} to {other.RangeEndAddress}). Two enabled scopes of "
                + "the same IP version and kind must not share an address; change the range or "
                + "disable the other scope first."
            : null;
    }
}
