using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Network;

/// <summary>
///     This static class provides the IPv4 address arithmetic of the DHCPv4 engine: converting
///     between an <see cref="IPAddress" /> and its 32-bit integer form to enumerate and compare
///     addresses, range checks, and same-subnet checks. The scope resolver, the allocation
///     service and the statistics service use it.
///     <para>
///         It does not use <see cref="IPNetwork" />, whose documentation does not promise that
///         host bits of an arbitrary base address are masked off, while scope ranges are
///         operator-entered addresses that need not sit on a network boundary.
///     </para>
/// </summary>
internal static class Dhcpv4NetworkHelper
{
    /// <summary>
    ///     This method converts an IPv4 address into its 32-bit unsigned integer form (network
    ///     byte order read as big-endian). It throws for a null or non-IPv4 address.
    /// </summary>
    /// <param name="address">The IPv4 address to convert.</param>
    /// <returns>The address as a 32-bit unsigned integer.</returns>
    public static uint ToUInt32(IPAddress address)
    {
        if (address is null || address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Must be an IPv4 address.", nameof(address));

        return BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
    }

    /// <summary>
    ///     This method converts a 32-bit unsigned integer back into an IPv4
    ///     <see cref="IPAddress" />, the inverse of <see cref="ToUInt32" />.
    /// </summary>
    /// <param name="value">The address as a 32-bit unsigned integer.</param>
    /// <returns>The equivalent <see cref="IPAddress" />.</returns>
    public static IPAddress FromUInt32(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }

    /// <summary>
    ///     This method reports whether two IPv4 addresses share their leading
    ///     <paramref name="prefixLength" /> bits, that is, are in the same subnet. It throws for a
    ///     prefix length outside 0-32.
    /// </summary>
    /// <param name="first">The first address to compare.</param>
    /// <param name="second">The second address to compare.</param>
    /// <param name="prefixLength">The CIDR prefix length, in bits (0-32), to compare over.</param>
    /// <returns>
    ///     <see langword="true" /> if both addresses share the same leading
    ///     <paramref name="prefixLength" /> bits; <see langword="false" /> otherwise.
    /// </returns>
    public static bool AreInSameNetwork(IPAddress first, IPAddress second, int prefixLength)
    {
        if (prefixLength is < 0 or > 32)
            throw new ArgumentOutOfRangeException(
                nameof(prefixLength),
                prefixLength,
                "A CIDR prefix length must be between 0 and 32, inclusive."
            );

        if (prefixLength == 0)
            return true;

        uint mask = prefixLength == 32 ? uint.MaxValue : ~(uint.MaxValue >> prefixLength);
        return (ToUInt32(first) & mask) == (ToUInt32(second) & mask);
    }

    /// <summary>
    ///     This method reports whether an address falls within an inclusive
    ///     [<paramref name="rangeStart" />, <paramref name="rangeEnd" />] address range.
    /// </summary>
    /// <param name="address">The address to check.</param>
    /// <param name="rangeStart">The first address in the range, inclusive.</param>
    /// <param name="rangeEnd">The last address in the range, inclusive.</param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="address" /> falls within the range;
    ///     <see langword="false" /> otherwise.
    /// </returns>
    public static bool IsWithinRange(IPAddress address, IPAddress rangeStart, IPAddress rangeEnd)
    {
        uint value = ToUInt32(address);
        return value >= ToUInt32(rangeStart) && value <= ToUInt32(rangeEnd);
    }

    /// <summary>
    ///     This method enumerates every address in an inclusive
    ///     [<paramref name="rangeStart" />, <paramref name="rangeEnd" />] address range, in
    ///     ascending order.
    /// </summary>
    /// <param name="rangeStart">The first address in the range, inclusive.</param>
    /// <param name="rangeEnd">The last address in the range, inclusive.</param>
    /// <returns>Every address in the range, ascending.</returns>
    public static IEnumerable<IPAddress> Enumerate(IPAddress rangeStart, IPAddress rangeEnd)
    {
        uint start = ToUInt32(rangeStart);
        uint end = ToUInt32(rangeEnd);

        for (uint value = start; value <= end; value++)
        {
            yield return FromUInt32(value);

            // Stop at 255.255.255.255; otherwise value would wrap to 0 and "value <= end" would
            // stay true forever.
            if (value == uint.MaxValue)
                yield break;
        }
    }
}
