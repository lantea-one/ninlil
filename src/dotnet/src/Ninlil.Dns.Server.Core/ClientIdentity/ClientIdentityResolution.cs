using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This record carries the result of a successful <see cref="IClientIdentityResolver.ResolveAsync" />:
///     the persistent identifier a client address resolved to and which identifier space it
///     belongs to. <see cref="ProtocolVersion" /> is needed because a dual-stack device's MAC
///     address (IPv4) and DUID (IPv6) are different strings for the same device;
///     <c>DnsClientEntity</c> is keyed by the pair <c>(ClientIdentifier, ProtocolVersion)</c> for
///     the same reason.
/// </summary>
/// <param name="ClientIdentifier">
///     The resolved identifier: a colon-separated hardware (MAC) address for
///     <see cref="InternetProtocolVersionEnum.IPv4" />, or a DHCP Unique Identifier (DUID) for
///     <see cref="InternetProtocolVersionEnum.IPv6" />.
/// </param>
/// <param name="ProtocolVersion">Which of the two disjoint identifier spaces <paramref name="ClientIdentifier" /> belongs to.</param>
public sealed record ClientIdentityResolution(
    string ClientIdentifier,
    InternetProtocolVersionEnum ProtocolVersion
);
