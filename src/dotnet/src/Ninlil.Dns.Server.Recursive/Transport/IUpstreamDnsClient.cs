using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Transport;

/// <summary>
///     This interface sends a single query to a single, already-known upstream name server --
///     the outbound, client-side counterpart to <c>Ninlil.Dns.Protocol.Plain</c>'s inbound
///     listeners, needed because a recursive resolver is itself a DNS *client* toward the rest
///     of the internet.
/// </summary>
public interface IUpstreamDnsClient
{
    /// <summary>
    ///     This method sends a single query and returns whatever response arrived, trying UDP
    ///     first and falling back to TCP if the UDP response came back truncated (RFC 1035
    ///     §4.2.1/§4.2.2).
    /// </summary>
    /// <param name="serverAddress">The upstream name server's own address.</param>
    /// <param name="name">The name being queried for.</param>
    /// <param name="type">The record type being queried for.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="dnssecOk">
    ///     Whether to set EDNS(0)'s "DNSSEC OK" bit, requesting RRSIG/NSEC/NSEC3/DNSKEY content
    ///     alongside the answer.
    /// </param>
    /// <param name="clientSubnetOption">
    ///     An RFC 7871 EDNS Client Subnet option to attach to this query, or <see langword="null" />
    ///     to send none -- see <c>Ninlil.Dns.Server.Core.ClientSubnet.EdnsClientSubnetService</c>,
    ///     this project's own builder for this value, gated by that feature's own
    ///     <c>Enable</c> toggle (off by default).
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The upstream server's response, or <see langword="null" /> if it timed out, refused
    ///     the connection, or replied with something too malformed to even decode.
    /// </returns>
    Task<DnsMessage> QueryAsync(
        IPAddress serverAddress,
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool dnssecOk,
        DnsEdnsOption clientSubnetOption = null,
        CancellationToken stoppingToken = default
    );
}
