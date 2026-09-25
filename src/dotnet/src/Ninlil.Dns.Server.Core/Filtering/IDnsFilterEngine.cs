using System.Net;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Filtering;

/// <summary>
///     This interface is block/allow-list filtering as seen by
///     <see cref="Pipeline.DnsQueryProcessingPipeline" />. It is defined here, without storage
///     types, and implemented by <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c>, so
///     <c>Server.Core</c> needs no repository reference.
///     <para>
///         The pipeline calls these methods around the dispatcher, so filtering covers every
///         resolver: authoritative, forwarded and recursive answers alike.
///     </para>
/// </summary>
public interface IDnsFilterEngine
{
    /// <summary>
    ///     This method checks a client's source address against the client-source block/allow
    ///     lists of the <c>DnsClientGroupEntity</c> it falls into. The pipeline calls it before
    ///     decoding the query and drops a denied client's query without a reply, so a blocked
    ///     source cannot confirm the server exists.
    /// </summary>
    /// <param name="remoteAddress">The client's source address.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <param name="clientId">
    ///     The DNS-over-HTTPS ClientID the query carried, or <see langword="null" /> for other
    ///     transports and for the bare <c>/dns-query</c> path. When present, a group matches by a
    ///     <c>ClientId</c> row in <c>DnsClientGroupNetworkEntity</c> as well as by a network that
    ///     contains <paramref name="remoteAddress" />; both are checked.
    /// </param>
    ValueTask<DnsFilterDecision> EvaluateClientAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default,
        string clientId = null
    );

    /// <summary>
    ///     This method checks a queried name against the domain block/allow lists of the client's
    ///     group. The pipeline calls it before dispatch, so a denied name is answered per
    ///     <see cref="Common.Enum.DnsFilterBlockResponseModeEnum" /> without any resolver seeing
    ///     it, even if this server is authoritative for it.
    /// </summary>
    /// <param name="queriedName">The queried domain name.</param>
    /// <param name="remoteAddress">The client's source address, used to find its group.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <param name="clientId">The DNS-over-HTTPS ClientID; see <see cref="EvaluateClientAsync" />.</param>
    ValueTask<DnsFilterDecision> EvaluateDomainAsync(
        DnsName queriedName,
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default,
        string clientId = null
    );

    /// <summary>
    ///     This method checks every A/AAAA record in a response's Answer section against the
    ///     response-IP block/allow lists of the client's group (response IP blocking). The
    ///     pipeline calls it after dispatch and replaces a denied response the same way as a
    ///     domain-list match. When CNAME cloaking protection is on, each CNAME target in the
    ///     Answer section is also checked against the client's domain lists, so an allowed name
    ///     that aliases a blocked one is blocked too.
    /// </summary>
    /// <param name="response">The response about to be sent.</param>
    /// <param name="remoteAddress">The client's source address, used to find its group.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <param name="clientId">The DNS-over-HTTPS ClientID; see <see cref="EvaluateClientAsync" />.</param>
    ValueTask<DnsFilterDecision> EvaluateResponseAsync(
        DnsMessage response,
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default,
        string clientId = null
    );

    /// <summary>
    ///     This method builds the denial response for a domain or response-IP match, following
    ///     the configured <see cref="Common.Enum.DnsFilterBlockResponseModeEnum" /> (NXDOMAIN or a
    ///     sinkhole A/AAAA record). The pipeline also uses it for pause and schedule blocks.
    /// </summary>
    /// <param name="request">The original request being denied.</param>
    DnsMessage BuildDeniedResponse(DnsMessage request);
}
