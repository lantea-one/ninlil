// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one client match rule of a <see cref="DnsClientGroupEntity" />. A
///     client matches a group by source address (<see cref="Cidr" />) or, for a DNS-over-HTTPS
///     client that sends a ClientID in the URL path, by an exact label match
///     (<see cref="ClientId" />). Each row sets exactly one of the two: a query always has a
///     source address, but only has a ClientID when it used the <c>/dns-query/{clientid}</c>
///     path.
/// </summary>
public class DnsClientGroupNetworkEntity : EntityBase
{
    /// <summary>
    ///     This property contains the <see cref="DnsClientGroupEntity" /> this rule belongs to.
    /// </summary>
    public Guid ClientGroupId { get; set; }

    /// <summary>
    ///     This property contains the network, in CIDR notation (a bare IP address is a
    ///     <c>/32</c>/<c>/128</c> network of one address). <see langword="null" /> for a row that
    ///     instead matches by <see cref="ClientId" />.
    /// </summary>
    public string Cidr { get; set; }

    /// <summary>
    ///     This property contains a DNS-over-HTTPS ClientID label, matched case-insensitively (as DNS
    ///     names are, RFC 1035 §3.1) against the <c>{clientid}</c> segment of
    ///     <c>/dns-query/{clientid}</c>. <see langword="null" /> for a row that matches by
    ///     <see cref="Cidr" />. Other transports (UDP, TCP, DoT, DoQ, DNSCrypt) carry no ClientID, so
    ///     such a row never matches their queries.
    /// </summary>
    public string ClientId { get; set; }
}
