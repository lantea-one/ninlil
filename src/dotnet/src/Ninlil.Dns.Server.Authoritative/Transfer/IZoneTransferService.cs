using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This interface serves zone transfers out, per RFC 5936 (AXFR). Unlike
///     <see cref="IDnsQueryResolver" />, this does not fit a single request-in/response-out
///     shape: a large zone's transfer response is a *sequence* of DNS messages, so this is
///     deliberately its own, separate contract rather than another <see cref="IDnsQueryResolver" />
///     implementation -- a caller (the TCP transport, or a test) is expected to recognize
///     an AXFR query before ever handing it to <see cref="IDnsQueryDispatcher" />, and to write
///     every yielded message back to the same TCP connection in order.
/// </summary>
public interface IZoneTransferService
{
    /// <summary>
    ///     This method builds a complete AXFR response as a sequence of messages.
    /// </summary>
    /// <param name="context">
    ///     The query to answer. Assumed, by the caller having routed here at all, to ask for
    ///     <see cref="DnsResourceRecordTypeEnum.Axfr" /> -- this method does not re-check that
    ///     itself.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The complete response, as one or more messages to send over the same TCP connection,
    ///     in order. A rejected transfer (wrong transport, missing or invalid TSIG, an unhosted
    ///     or mismatched zone name) is still exactly one yielded message, carrying the
    ///     appropriate non-success response code, rather than an empty sequence or a thrown
    ///     exception.
    /// </returns>
    IAsyncEnumerable<DnsMessage> CreateAxfrResponseAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method builds a complete IXFR response (RFC 1995) as a sequence of messages,
    ///     falling back to a full, AXFR-shaped transfer when an incremental one can't be served
    ///     (the client's claimed serial isn't covered by this zone's change journal).
    /// </summary>
    /// <param name="context">
    ///     The query to answer. Assumed, by the caller having routed here at all, to ask for
    ///     <see cref="DnsResourceRecordTypeEnum.Ixfr" />, and to carry the client's own current
    ///     SOA record in its Authority section per RFC 1995 §3 -- this method does not re-check
    ///     the query type itself, but does validate the SOA is actually present.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The complete response, as one or more messages to send over the same TCP connection,
    ///     in order, on exactly the same terms as <see cref="CreateAxfrResponseAsync" />.
    /// </returns>
    IAsyncEnumerable<DnsMessage> CreateIxfrResponseAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    );
}
