using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Pipeline;

/// <summary>
///     This record carries one undecoded query into <see cref="DnsQueryProcessingPipeline" />.
///     Every transport (plain UDP/TCP, DNS-over-HTTPS, DNS-over-TLS, DNS-over-QUIC, DNSCrypt)
///     produces the same shape, which lets one pipeline serve them all.
/// </summary>
/// <param name="RequestBytes">The raw, not-yet-decoded query bytes.</param>
/// <param name="Protocol">The transport this query arrived over.</param>
/// <param name="RemoteAddress">
///     The querying client's IP address. It is stored in its canonical form
///     (<see cref="ClientAddress.Normalize" />): an IPv4-mapped IPv6 address becomes plain IPv4
///     and an IPv6 scope id is dropped. A dual-stack listener socket reports every IPv4 client as
///     <c>::ffff:a.b.c.d</c>, and normalizing here, for the constructor and for <c>with</c>
///     copies, means no transport and no later stage has to unmap. The listeners keep the raw
///     endpoint to send the reply.
/// </param>
/// <param name="RespondAsync">
///     The delegate used to send the eventual response back. The pipeline calls it, or
///     <see cref="OnDropped" />, exactly once per query.
/// </param>
/// <param name="ClientId">
///     The client identifier the query carried, or <see langword="null" />. Only DNS-over-HTTPS
///     sets it, from the <c>/dns-query/{clientid}</c> path; the filter engine uses it to pick
///     the client's rules.
/// </param>
public sealed record DnsInboundMessage(
    ReadOnlyMemory<byte> RequestBytes,
    DnsTransportProtocolEnum Protocol,
    IPAddress RemoteAddress,
    DnsResponseSenderAsync RespondAsync,
    string ClientId = null
)
{
    /// <summary>
    ///     This property contains the querying client's IP address in canonical form; see the
    ///     <c>RemoteAddress</c> parameter. The initializer covers the constructor, and the
    ///     <see langword="init" /> accessor covers <c>with</c> expressions.
    /// </summary>
    public IPAddress RemoteAddress
    {
        get;
        init => field = ClientAddress.Normalize(value);
    } = ClientAddress.Normalize(RemoteAddress);

    /// <summary>
    ///     This property contains the delegate the pipeline calls instead of
    ///     <see cref="RespondAsync" /> when it ends the query without a reply (see
    ///     <see cref="DnsQueryDropReasonEnum" />). It is <see langword="null" /> for transports
    ///     that need no signal, such as UDP, where a missing reply is the answer.
    /// </summary>
    public DnsQueryDropNotifier OnDropped { get; init; }

    /// <summary>
    ///     This property is <see langword="true" /> when the query arrived in a single datagram.
    ///     The pipeline then applies the UDP size rules to the reply: truncation to the client's
    ///     EDNS payload size, the amplification ceiling and the subnet limiter's UDP mode. It
    ///     defaults to <see langword="true" /> for <see cref="DnsTransportProtocolEnum.Udp" />. A
    ///     transport that carries datagrams under another protocol value (DNSCrypt over UDP) sets
    ///     it explicitly.
    /// </summary>
    public bool IsDatagram { get; init; } = Protocol == DnsTransportProtocolEnum.Udp;

    /// <summary>
    ///     This property contains the largest reply the transport can carry, in octets, or
    ///     <see langword="null" /> for no transport limit. The pipeline truncates a datagram reply
    ///     to fit it, and drops the query with <see cref="DnsQueryDropReasonEnum.ResponseTooLarge" />
    ///     rather than send a reply that is still larger.
    /// </summary>
    public int? MaxResponseOctets { get; init; }
}
