using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity;

/// <summary>
///     This class represents one real, resolved (RFC 826 "complete") row of Linux's own
///     kernel-exposed ARP neighbor table (<c>/proc/net/arp</c>) -- an IPv4 address paired with
///     the hardware (MAC) address the kernel has genuinely seen answer for it, as opposed to an
///     entry the kernel is still waiting on a reply for.
/// </summary>
/// <param name="IpAddress">The IPv4 address this entry maps.</param>
/// <param name="HardwareAddress">
///     The colon-separated hardware (MAC) address the kernel resolved <paramref name="IpAddress" />
///     to, exactly as the kernel itself formats it (lowercase hex, colon-separated octets).
/// </param>
public sealed record ArpNeighborTableEntry(IPAddress IpAddress, string HardwareAddress);
