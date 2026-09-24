// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single client match rule that belongs to a
///     <see cref="DnsClientGroupEntity" /> -- a client matches a group either by its own source
///     address (<see cref="Cidr" />) or, for a DNS-over-HTTPS client presenting a URL-path
///     ClientID, by an exact label match (<see cref="ClientId" />). Exactly one of the two
///     properties is expected to be set per row -- a row is either a network row or a ClientID
///     row, never both, mirroring how a single query can only ever carry one or the other kind of
///     identity (a plain source address always; a DoH ClientID only when the client actually used
///     the <c>/dns-query/{clientid}</c> path).
/// </summary>
public class DnsClientGroupNetworkEntity : EntityBase
{
    /// <summary>
    ///     This property contains the <see cref="DnsClientGroupEntity" /> this network belongs
    ///     to.
    /// </summary>
    public Guid ClientGroupId { get; set; }

    /// <summary>
    ///     This property contains the network, in CIDR notation (a bare IP address is a
    ///     <c>/32</c>/<c>/128</c> network of one address). <see langword="null" /> for a row that
    ///     instead matches by <see cref="ClientId" />.
    /// </summary>
    public string Cidr { get; set; }

    /// <summary>
    ///     This property contains a DNS-over-HTTPS ClientID label this row matches exactly
    ///     (case-insensitively -- the same convention this project's own DNS name comparison,
    ///     <c>Ninlil.Dns.Common.Wire.DnsName.Equals</c>, already uses per RFC 1035 §3.1) against the
    ///     <c>{clientid}</c> URL-path segment a client presented when it queried through
    ///     <c>/dns-query/{clientid}</c>. <see langword="null" /> for a row that instead matches by
    ///     <see cref="Cidr" />. Only ever populated for a DNS-over-HTTPS client -- every other
    ///     transport (plain UDP/TCP, DoT, DoQ, DNSCrypt) has no client-supplied identity beyond
    ///     its own source address, so a ClientID-kind row can never match a query that arrived
    ///     over one of them.
    /// </summary>
    public string ClientId { get; set; }
}
