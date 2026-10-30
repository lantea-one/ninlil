using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Transport;

/// <summary>
///     This interface sends one query to one upstream name server. It is how
///     <see cref="Resolution.IterativeResolutionEngine" /> and the root-hints priming talk to
///     authoritative servers; <see cref="UpstreamDnsClient" /> implements it.
/// </summary>
public interface IUpstreamDnsClient
{
    /// <summary>
    ///     This method sends one non-recursive query with EDNS and returns the response, trying UDP
    ///     first and retrying over TCP if the UDP response is truncated (RFC 1035 §4.2.1 and
    ///     §4.2.2 for the TC bit and the TCP framing, RFC 1123 §6.1.3.2 for the retry).
    /// </summary>
    /// <param name="serverAddress">The upstream name server's address.</param>
    /// <param name="name">The name being queried for.</param>
    /// <param name="type">The record type being queried for.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="dnssecOk">
    ///     Whether to set EDNS(0)'s "DNSSEC OK" bit, requesting RRSIG/NSEC/NSEC3/DNSKEY content
    ///     alongside the answer.
    /// </param>
    /// <param name="clientSubnetOption">
    ///     An RFC 7871 EDNS Client Subnet option to attach, or <see langword="null" /> to send none
    ///     (built by <c>Ninlil.Dns.Server.Core.ClientSubnet.EdnsClientSubnetService</c>).
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The upstream server's response, or <see langword="null" /> if it timed out, refused
    ///     the connection, sent something that does not decode, or answered with the wrong
    ///     message ID.
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
