using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This class carries the outcome of a successful <see cref="IClientIdentityResolver.ResolveAsync" />
///     call: the persistent identifier a querying client's own source address was resolved to,
///     plus which of the two disjoint identifier spaces it belongs to.
///     <see cref="ProtocolVersion" /> exists specifically because a dual-stack device's IPv4
///     hardware (MAC) address and IPv6 DHCP Unique Identifier (DUID) are two different strings
///     that must never be confused with one another -- the same reason
///     <c>Ninlil.Dhcp.Data.Abstractions.Entity.LeaseEntity</c> keeps a v4 and a v6 lease for the
///     same physical device as two separate rows rather than one, and the same reason
///     <c>DnsClientEntity</c> (<c>Ninlil.Dns.Data.Abstractions</c>) keys a known client by the
///     pair <c>(ClientIdentifier, ProtocolVersion)</c> rather than by <c>ClientIdentifier</c>
///     alone.
/// </summary>
/// <param name="ClientIdentifier">
///     The resolved identifier itself -- a colon-separated hardware (MAC) address for
///     <see cref="InternetProtocolVersionEnum.IPv4" />, or a DHCP Unique Identifier (DUID) for
///     <see cref="InternetProtocolVersionEnum.IPv6" />.
/// </param>
/// <param name="ProtocolVersion">Which of the two disjoint identifier spaces <paramref name="ClientIdentifier" /> belongs to.</param>
public sealed record ClientIdentityResolution(
    string ClientIdentifier,
    InternetProtocolVersionEnum ProtocolVersion
);
