using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Pipeline;

/// <summary>
///     This record carries one undecoded query into <see cref="DnsQueryProcessingPipeline" />.
///     Every transport (plain UDP/TCP, DNS-over-HTTPS, DNS-over-TLS, DNS-over-QUIC, DNSCrypt)
///     produces the same shape, which lets one pipeline serve them all.
/// </summary>
/// <param name="RequestBytes">The raw, not-yet-decoded query bytes.</param>
/// <param name="Protocol">The transport this query arrived over.</param>
/// <param name="RemoteAddress">The querying client's IP address.</param>
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
);
