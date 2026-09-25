using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This interface records a passive sighting of a client: evidence from something the
///     server already sees that a device was on the network at a given time. Every presence
///     signal feeds it: the kernel neighbor table, DHCP lease grants and renewals, and DNS query
///     traffic. The implementation is <c>ClientPresenceTracker</c> in
///     <c>Ninlil.Dns.Server.ClientIdentity</c>.
///     <para>
///         <b>Every method is memory-only and returns immediately.</b> Sightings are merged in
///         memory and saved later in bulk by a background flush, so a caller on the query path can
///         record one per query cheaply. Each signal records what it knows; matching a sighting to
///         a known client happens at flush time.
///     </para>
/// </summary>
public interface IClientPresenceRecorder
{
    /// <summary>
    ///     This method records a sighting of a client whose persistent identifier is already
    ///     known, such as a DHCP lease grant or renewal.
    /// </summary>
    /// <param name="clientIdentifier">The client's identifier: a MAC address for an IPv4 identity, or a DUID for an IPv6 one.</param>
    /// <param name="protocolVersion">Which of the two identifier spaces <paramref name="clientIdentifier" /> belongs to.</param>
    /// <param name="address">The IP address the client was seen at, or <see langword="null" /> if unknown.</param>
    /// <param name="seenAtUtc">The instant the client was seen.</param>
    void RecordIdentity(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        IPAddress address,
        DateTimeOffset seenAtUtc
    );

    /// <summary>
    ///     This method records a sighting of a hardware (MAC) address, such as a reachable kernel
    ///     neighbor-table entry. At flush time it is matched against every known client whose
    ///     identifier contains the same MAC.
    /// </summary>
    /// <param name="macAddress">The 6-octet hardware address seen.</param>
    /// <param name="address">The IP address it was seen at.</param>
    /// <param name="seenAtUtc">The instant it was seen.</param>
    void RecordHardwareAddress(byte[] macAddress, IPAddress address, DateTimeOffset seenAtUtc);

    /// <summary>
    ///     This method records traffic from a source address whose identity is not yet known, such
    ///     as a DNS query. The address is resolved to a client at flush time, once per distinct
    ///     address rather than once per query.
    /// </summary>
    /// <param name="address">The source address traffic arrived from.</param>
    /// <param name="seenAtUtc">The instant the traffic arrived.</param>
    void RecordSourceAddress(IPAddress address, DateTimeOffset seenAtUtc);
}
