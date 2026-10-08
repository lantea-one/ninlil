// Define our namespace.
namespace Ninlil.Dns.Server.Core.Pipeline;

/// <summary>
///     This enum names why <see cref="DnsQueryProcessingPipeline" /> ended a query without
///     sending a reply. The pipeline reports it through <see cref="DnsQueryDropNotifier" />, so a
///     transport that holds a request open (DNS-over-HTTPS, DNS-over-QUIC streams) can finish it
///     in the way its protocol allows instead of waiting for a reply that never comes. Datagram
///     transports ignore it: silence is their answer.
/// </summary>
public enum DnsQueryDropReasonEnum
{
    /// <summary>
    ///     The per-IP token budget is exhausted (<see cref="RateLimiting.IDnsRateLimiter.TryAcquireAsync" />).
    /// </summary>
    RateLimited,

    /// <summary>
    ///     The client's subnet budget is exhausted and the limiter chose to drop rather than send
    ///     a truncated reply.
    /// </summary>
    SubnetRateLimited,

    /// <summary>
    ///     The client-source access check denied the client. The query gets no reply, so a blocked
    ///     source learns nothing about this server.
    /// </summary>
    ClientDenied,

    /// <summary>
    ///     The message could not be decoded as DNS.
    /// </summary>
    Malformed,

    /// <summary>
    ///     The response is larger than <see cref="DnsInboundMessage.MaxResponseOctets" />, even
    ///     after truncation, so it cannot be sent.
    /// </summary>
    ResponseTooLarge,

    /// <summary>
    ///     An unexpected failure ended the query: an exception in a resolver or filter, or a
    ///     shutdown in the middle of processing.
    /// </summary>
    Failed,
}
