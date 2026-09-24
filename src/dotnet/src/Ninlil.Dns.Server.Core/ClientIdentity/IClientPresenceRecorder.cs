using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This interface records a passive sighting of a client -- evidence, from a signal this
///     server already observes, that a device was present on the network at a given instant. It
///     is the single entry point every presence signal feeds: the kernel neighbor table, DHCP lease
///     grants and renewals, and live DNS query traffic.
///     <para>
///         <b>Every method is memory-only and returns immediately.</b> A sighting is coalesced into
///         an in-memory buffer and persisted later, in bulk, by a background flush -- never written
///         synchronously -- so a caller on the hot query-answering path can record one per query at
///         negligible cost, per this project's own "the event bus never waits on a slow subscriber"
///         invariant. Each signal records whatever it genuinely knows; mapping a sighting onto the
///         owning known-client address happens at flush time.
///     </para>
/// </summary>
public interface IClientPresenceRecorder
{
    /// <summary>
    ///     This method records a sighting of a client whose persistent identifier is already known
    ///     -- a DHCP lease grant or renewal, for example.
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
    ///     This method records a sighting of a hardware (MAC) address -- a kernel neighbor-table
    ///     entry confirmed reachable, for example -- which is matched at flush time against every
    ///     known address whose identifier embeds the same MAC.
    /// </summary>
    /// <param name="macAddress">The 6-octet hardware address seen.</param>
    /// <param name="address">The IP address it was seen at.</param>
    /// <param name="seenAtUtc">The instant it was seen.</param>
    void RecordHardwareAddress(byte[] macAddress, IPAddress address, DateTimeOffset seenAtUtc);

    /// <summary>
    ///     This method records traffic from a source IP address whose identity is not yet known --
    ///     a DNS query, for example. The address is resolved to a client identity at flush time,
    ///     once per distinct address rather than once per query.
    /// </summary>
    /// <param name="address">The source address traffic arrived from.</param>
    /// <param name="seenAtUtc">The instant the traffic arrived.</param>
    void RecordSourceAddress(IPAddress address, DateTimeOffset seenAtUtc);
}
