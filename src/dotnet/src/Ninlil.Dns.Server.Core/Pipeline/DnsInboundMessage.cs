using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Pipeline;

/// <summary>
///     This class carries one not-yet-decoded query into <see cref="DnsQueryProcessingPipeline" />.
///     Every transport -- plain UDP/TCP, DNS-over-HTTPS, DNS-over-TLS, DNS-over-QUIC, DNSCrypt --
///     produces this same shape regardless of how differently each one actually receives bytes
///     off the network, which is what lets one shared pipeline serve all of them.
/// </summary>
/// <param name="RequestBytes">The raw, not-yet-decoded query bytes.</param>
/// <param name="Protocol">The transport this query arrived over.</param>
/// <param name="RemoteAddress">The querying client's IP address.</param>
/// <param name="RespondAsync">The delegate used to send the eventual response back.</param>
/// <param name="ClientId">
///     The client-supplied identifier this query carried, or <see langword="null" /> when it
///     carried none. Only DNS-over-HTTPS ever populates this (a client that queried through
///     <c>/dns-query/{clientid}</c> rather than the bare <c>/dns-query</c> path) -- every other
///     transport passes <see langword="null" /> here, identical to today's behavior before this
///     property existed.
/// </param>
public sealed record DnsInboundMessage(
    ReadOnlyMemory<byte> RequestBytes,
    DnsTransportProtocolEnum Protocol,
    IPAddress RemoteAddress,
    DnsResponseSenderAsync RespondAsync,
    string ClientId = null
);
