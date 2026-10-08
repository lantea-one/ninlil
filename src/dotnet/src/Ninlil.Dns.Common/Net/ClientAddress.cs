using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Common.Net;

/// <summary>
///     This static class gives every part of Ninlil one canonical form of a client IP address, and
///     one way to match an address against a configured address or CIDR network. It exists
///     because a dual-stack listener socket reports an IPv4 client as an IPv4-mapped IPv6 address
///     (<c>::ffff:192.0.2.9</c>), and a link-local IPv6 client with a zone index
///     (<c>fe80::1%2</c>). Neither form equals or falls inside what an operator types
///     (<c>192.0.2.9</c>, <c>192.0.2.0/24</c>, <c>fe80::1</c>), so access control, rate-limit
///     bypass, NOTIFY checks, client identity and subnet grouping would otherwise miss them.
///     <para>
///         The pipeline entry points (<c>DnsInboundMessage</c>, <c>DnsQueryContext</c>) and the
///         web session code call <see cref="Normalize" /> once, so everything behind them sees
///         the canonical form. Code that receives addresses from elsewhere (stored addresses,
///         configuration) calls the helpers here itself.
///     </para>
/// </summary>
public static class ClientAddress
{
    /// <summary>
    ///     This method returns the canonical form of an address: an IPv4-mapped IPv6 address
    ///     becomes plain IPv4, and an IPv6 address loses its scope id. Any other address is
    ///     returned as is, without allocating.
    /// </summary>
    /// <param name="address">The address to normalize, or <see langword="null" />.</param>
    /// <returns>The canonical address, or <see langword="null" /> for <see langword="null" />.</returns>
    public static IPAddress Normalize(IPAddress address)
    {
        if (address == null)
            return null;

        if (address.IsIPv4MappedToIPv6)
            return address.MapToIPv4();

        // IPAddress.Equals compares the scope id, so fe80::1%2 would never equal fe80::1.
        if (
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            && address.ScopeId != 0
        )
            return new IPAddress(address.GetAddressBytes());

        return address;
    }

    /// <summary>
    ///     This method says whether two addresses are the same client address once both are
    ///     normalized. Two <see langword="null" /> values are not the same address.
    /// </summary>
    public static bool IsSameAddress(IPAddress first, IPAddress second)
    {
        if (first == null || second == null)
            return false;

        return Normalize(first).Equals(Normalize(second));
    }

    /// <summary>
    ///     This method checks whether a network entry (a bare address, matched exactly, or a CIDR
    ///     network, matched by containment) covers an address. Both sides are normalized first, so
    ///     an IPv4-mapped address matches an IPv4 entry. An entry that does not parse matches
    ///     nothing, so one bad value cannot break query handling.
    /// </summary>
    /// <param name="entry">The configured address or CIDR network.</param>
    /// <param name="address">The client address to test.</param>
    public static bool NetworkContains(string entry, IPAddress address)
    {
        if (string.IsNullOrWhiteSpace(entry) || address == null)
            return false;

        IPAddress normalizedAddress = Normalize(address);

        if (entry.Contains('/'))
        {
            if (!IPNetwork.TryParse(entry, out IPNetwork network))
                return false;

            // An IPv4-mapped network (::ffff:192.0.2.0/120) is the same set as the IPv4 one, but
            // IPNetwork compares families, so compare it against the mapped form of the address.
            IPAddress candidate =
                network.BaseAddress.IsIPv4MappedToIPv6
                && normalizedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    ? normalizedAddress.MapToIPv6()
                    : normalizedAddress;

            return network.Contains(candidate);
        }

        return IPAddress.TryParse(entry.Trim(), out IPAddress parsed)
            && Normalize(parsed).Equals(normalizedAddress);
    }
}
