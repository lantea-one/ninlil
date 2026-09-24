using System.Net;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Filtering;

/// <summary>
///     This interface is <c>Server.Core</c>'s own Data.Abstractions-free contract for
///     block/allow-list filtering, implemented by <c>Ninlil.Dns.Server.Filtering</c> (which
///     does depend on <c>Data.Abstractions</c>) and consulted by
///     <see cref="Pipeline.DnsQueryProcessingPipeline" /> -- the same "define the abstraction
///     here, implement it in a dedicated project that may depend on storage" shape
///     <see cref="Certificates.TlsCertificateLoader" /> and the event bus already establish,
///     kept consistent so <c>Server.Core</c> itself never needs a repository reference.
///     <para>
///         All three methods apply uniformly to both <c>Server.Authoritative</c> and
///         <c>Server.Recursive</c>, since the pipeline calls them before either engine ever sees
///         the query (per the user's explicit decision that filtering covers both engines).
///     </para>
/// </summary>
public interface IDnsFilterEngine
{
    /// <summary>
    ///     This method checks a querying client's own source address against every configured
    ///     client-source block/allow list, resolved through whichever
    ///     <c>DnsClientGroupEntity</c> that address falls into. Called before the query is even
    ///     decoded past its header -- a denied client's query is dropped exactly like this
    ///     project's own DNSCrypt transport drops an unauthenticated packet, not answered
    ///     REFUSED, to avoid confirming this server's own existence to a blocked source.
    /// </summary>
    /// <param name="remoteAddress">The querying client's own source address.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <param name="clientId">
    ///     The DNS-over-HTTPS ClientID this query carried, or <see langword="null" /> for every
    ///     other transport (or a DoH client that queried through the bare <c>/dns-query</c> path).
    ///     When present, client-group resolution tries a <c>ClientId</c>-labeled
    ///     <c>DnsClientGroupNetworkEntity</c> row first, alongside (not instead of) the existing
    ///     CIDR-containment match against <paramref name="remoteAddress" /> -- a client matches a
    ///     group by either kind of row, so both are always checked.
    /// </param>
    ValueTask<DnsFilterDecision> EvaluateClientAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default,
        string clientId = null
    );

    /// <summary>
    ///     This method checks a queried domain name against every configured domain block/allow
    ///     list assigned to the querying client's own group. Called after
    ///     <see cref="EvaluateClientAsync" /> passes, before the query reaches
    ///     <see cref="Resolution.IDnsQueryDispatcher" /> -- a denied domain is answered per the
    ///     configured <see cref="Common.Enum.DnsFilterBlockResponseModeEnum" /> without either
    ///     engine ever seeing the query, so a blocked domain is blocked even if this server is
    ///     itself authoritative for it.
    /// </summary>
    /// <param name="queriedName">The queried domain name.</param>
    /// <param name="remoteAddress">The querying client's own source address, for client-group resolution.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <param name="clientId">The DNS-over-HTTPS ClientID this query carried -- see <see cref="EvaluateClientAsync" />'s own doc comment.</param>
    ValueTask<DnsFilterDecision> EvaluateDomainAsync(
        DnsName queriedName,
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default,
        string clientId = null
    );

    /// <summary>
    ///     This method checks every A/AAAA record in an already-produced response's own Answer
    ///     section against every configured response-IP block/allow list assigned to the
    ///     querying client's own group (Technitium's "response IP blocking"). Called after
    ///     <see cref="Resolution.IDnsQueryDispatcher.DispatchAsync" /> returns, before the
    ///     response is encoded and sent -- a denied response is replaced per the configured
    ///     <see cref="Common.Enum.DnsFilterBlockResponseModeEnum" /> exactly as a domain-list
    ///     match would be, since by this point the distinction no longer matters to the client.
    ///     When CNAME-cloaking protection is on, every CNAME target in the Answer section is
    ///     also checked against the client's own domain lists, so a permitted name that merely
    ///     aliases a blocked one is blocked too.
    /// </summary>
    /// <param name="response">The response about to be sent.</param>
    /// <param name="remoteAddress">The querying client's own source address, for client-group resolution.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <param name="clientId">The DNS-over-HTTPS ClientID this query carried -- see <see cref="EvaluateClientAsync" />'s own doc comment.</param>
    ValueTask<DnsFilterDecision> EvaluateResponseAsync(
        DnsMessage response,
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default,
        string clientId = null
    );

    /// <summary>
    ///     This method builds the actual denial response for a domain or response-IP match,
    ///     honoring the currently configured <see cref="Common.Enum.DnsFilterBlockResponseModeEnum" />
    ///     (NXDOMAIN, or a sinkhole A/AAAA record).
    /// </summary>
    /// <param name="request">The original request being denied.</param>
    DnsMessage BuildDeniedResponse(DnsMessage request);
}
