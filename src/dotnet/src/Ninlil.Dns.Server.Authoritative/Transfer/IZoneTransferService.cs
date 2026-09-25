using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This interface serves outgoing zone transfers: AXFR (RFC 5936) and IXFR (RFC 1995). A
///     transfer answer is a sequence of messages, which does not fit
///     <see cref="IDnsQueryResolver" />, so the TCP, TLS and QUIC listeners recognize AXFR and
///     IXFR queries themselves, call this instead of <see cref="IDnsQueryDispatcher" />, and
///     write each yielded message to the connection in order. The implementation is
///     <see cref="ZoneTransferService" />.
/// </summary>
public interface IZoneTransferService
{
    /// <summary>
    ///     This method builds a complete AXFR response as a sequence of messages.
    /// </summary>
    /// <param name="context">
    ///     The query to answer. The caller has already checked that it asks for
    ///     <see cref="DnsResourceRecordTypeEnum.Axfr" />; this method does not check again.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The response as one or more messages to send in order on the same connection. A
    ///     rejected transfer (wrong transport, missing or invalid TSIG, unknown zone) yields one
    ///     message with an error code rather than nothing or an exception.
    /// </returns>
    IAsyncEnumerable<DnsMessage> CreateAxfrResponseAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method builds an IXFR response (RFC 1995) as a sequence of messages. It sends a
    ///     full, AXFR-style transfer when the client's serial is not covered by the change journal
    ///     or the zone is signed.
    /// </summary>
    /// <param name="context">
    ///     The query to answer. The caller has checked that it asks for
    ///     <see cref="DnsResourceRecordTypeEnum.Ixfr" />. It should carry the client's current SOA
    ///     in its Authority section (RFC 1995 §3); this method checks that it does.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The response as one or more messages, as for <see cref="CreateAxfrResponseAsync" />.
    /// </returns>
    IAsyncEnumerable<DnsMessage> CreateIxfrResponseAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    );
}
