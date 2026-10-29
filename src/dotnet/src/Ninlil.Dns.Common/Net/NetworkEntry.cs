using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Common.Net;

/// <summary>
///     This class is one configured address or CIDR network, parsed once, so a query path that
///     tests a client address against it parses nothing. It exists because the access-control
///     rules, client groups, filter lists, static address mappings and rate limiter all hold
///     operator-typed network text and match it for every query; parsing that text per query
///     costs allocations and time that load-time parsing avoids.
///     <para>
///         <see cref="TryParse" /> followed by <see cref="Contains" /> must give exactly the
///         answer <see cref="ClientAddress.NetworkContains" /> gives for the same text and
///         address, which is why <see cref="ClientAddress.NetworkContains" /> delegates here.
///         The rules therefore stay in one place: an entry with a slash is a CIDR network matched
///         by containment, any other entry is a bare address matched exactly, both sides are
///         normalized (<see cref="ClientAddress.Normalize" />) so an IPv4-mapped address matches
///         an IPv4 entry, and an entry that does not parse never matches.
///     </para>
///     <para>
///         An instance is immutable, so one instance is shared by every query thread.
///     </para>
/// </summary>
public sealed class NetworkEntry
{
    /// <summary>
    ///     This field is the CIDR network, or <see langword="default" /> for a bare address.
    /// </summary>
    private readonly IPNetwork _network;

    /// <summary>
    ///     This field says whether <see cref="_network" /> is an IPv4-mapped IPv6 network
    ///     (<c>::ffff:192.0.2.0/120</c>). <see cref="IPNetwork.Contains" /> compares address
    ///     families, so an IPv4 client address is mapped to IPv6 before the test.
    /// </summary>
    private readonly bool _isMappedNetwork;

    /// <summary>
    ///     This field is the normalized bare address, or <see langword="null" /> for a network.
    /// </summary>
    private readonly IPAddress _address;

    /// <summary>
    ///     This constructor wraps a parsed CIDR network.
    /// </summary>
    private NetworkEntry(IPNetwork network)
    {
        _network = network;
        _isMappedNetwork = network.BaseAddress.IsIPv4MappedToIPv6;
    }

    /// <summary>
    ///     This constructor wraps a parsed, normalized bare address.
    /// </summary>
    private NetworkEntry(IPAddress address) => _address = address;

    /// <summary>
    ///     This method parses a configured entry: a bare IPv4 or IPv6 address (surrounding spaces
    ///     allowed) or a CIDR network.
    /// </summary>
    /// <param name="entry">The configured text; <see langword="null" /> and blank do not parse.</param>
    /// <param name="network">The parsed entry, or <see langword="null" /> when the method returns <see langword="false" />.</param>
    /// <returns><see langword="true" /> when the text is a valid address or network.</returns>
    public static bool TryParse(string entry, out NetworkEntry network)
    {
        network = null;

        if (string.IsNullOrWhiteSpace(entry))
            return false;

        if (entry.Contains('/'))
        {
            if (!IPNetwork.TryParse(entry, out IPNetwork parsedNetwork))
                return false;

            network = new NetworkEntry(parsedNetwork);
            return true;
        }

        if (!IPAddress.TryParse(entry.Trim(), out IPAddress parsedAddress))
            return false;

        network = new NetworkEntry(ClientAddress.Normalize(parsedAddress));
        return true;
    }

    /// <summary>
    ///     This method says whether the entry covers an address: a network by containment, a bare
    ///     address by equality, after the address is normalized.
    /// </summary>
    /// <param name="address">The client address to test; <see langword="null" /> matches nothing.</param>
    public bool Contains(IPAddress address)
    {
        if (address == null)
            return false;

        IPAddress normalizedAddress = ClientAddress.Normalize(address);

        if (_address != null)
            return _address.Equals(normalizedAddress);

        // An IPv4-mapped network is the same set as the IPv4 one, so an IPv4 address is tested in
        // its mapped form.
        IPAddress candidate =
            _isMappedNetwork && normalizedAddress.AddressFamily == AddressFamily.InterNetwork
                ? normalizedAddress.MapToIPv6()
                : normalizedAddress;

        return _network.Contains(candidate);
    }
}
