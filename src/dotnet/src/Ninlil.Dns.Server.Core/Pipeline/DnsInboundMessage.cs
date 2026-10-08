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
/// <param name="RespondAsync">The delegate used to send the eventual response back.</param>
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
}
