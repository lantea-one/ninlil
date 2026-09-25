using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity;

/// <summary>
///     This record is one complete row of the Linux kernel's ARP table (<c>/proc/net/arp</c>): an
///     IPv4 address and the hardware (MAC) address the kernel has seen answer for it.
///     <see cref="ArpNeighborTableParser" /> skips rows still waiting for a reply.
/// </summary>
/// <param name="IpAddress">The IPv4 address this entry maps.</param>
/// <param name="HardwareAddress">
///     The colon-separated hardware (MAC) address the kernel resolved <paramref name="IpAddress" />
///     to, as the kernel formats it (lowercase hex, colon-separated octets).
/// </param>
public sealed record ArpNeighborTableEntry(IPAddress IpAddress, string HardwareAddress);
