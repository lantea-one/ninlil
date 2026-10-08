using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This record carries one decoded query and how it arrived.
///     <see cref="Pipeline.DnsQueryProcessingPipeline" /> builds it after decoding and passes it
///     through <see cref="IDnsQueryDispatcher" /> to each <see cref="IDnsQueryResolver" />.
/// </summary>
/// <param name="Request">The decoded query.</param>
/// <param name="Protocol">The transport the query arrived over.</param>
/// <param name="RemoteAddress">
///     The client's IP address in canonical form (<see cref="ClientAddress.Normalize" />): an
///     IPv4-mapped IPv6 address is plain IPv4 and an IPv6 scope id is dropped, so resolvers match
///     it against configured addresses and networks without unmapping. The port is left out
///     because only the transport needs it to reply, and resolvers decide on the address alone
///     (access control, for example).
/// </param>
/// <param name="ReceivedAtUtc">The UTC date and time the query was received.</param>
/// <param name="RawMessage">
///     The wire bytes <paramref name="Request" /> was decoded from, or <see langword="null" />.
///     TSIG verification (zone transfers, dynamic update) needs them because the MAC covers the
///     bytes as received (RFC 8945 §4.3), and re-serializing <paramref name="Request" /> could
///     differ, for example in name compression. The pipeline always sets it.
/// </param>
/// <param name="TsigRecordStartOffset">
///     The offset within <paramref name="RawMessage" /> a TSIG record started at, or -1 if
///     <paramref name="Request" /> carried no TSIG record (including when <paramref name="RawMessage" />
///     itself is <see langword="null" />).
/// </param>
/// <param name="ClientId">
///     The client identifier the query carried, or <see langword="null" />; see
///     <see cref="Pipeline.DnsInboundMessage.ClientId" />.
/// </param>
public record DnsQueryContext(
    DnsMessage Request,
    DnsTransportProtocolEnum Protocol,
    IPAddress RemoteAddress,
    DateTimeOffset ReceivedAtUtc,
    byte[] RawMessage = null,
    int TsigRecordStartOffset = -1,
    string ClientId = null
)
{
    /// <summary>
    ///     This property contains the outcome of checking the request's TSIG record, or
    ///     <see langword="null" /> when the request is unsigned or the pipeline has no
    ///     <see cref="IDnsTransactionSignatureService" />. A resolver that requires TSIG (dynamic
    ///     update) must treat anything but <c>IsVerified: true</c> as unauthenticated.
    /// </summary>
    public DnsTransactionSignature TransactionSignature { get; init; }

    /// <summary>
    ///     This property contains the client's IP address in canonical form; see the
    ///     <c>RemoteAddress</c> parameter. The initializer covers the constructor, and the
    ///     <see langword="init" /> accessor covers <c>with</c> expressions.
    /// </summary>
    public IPAddress RemoteAddress
    {
        get;
        init => field = ClientAddress.Normalize(value);
    } = ClientAddress.Normalize(RemoteAddress);
}
