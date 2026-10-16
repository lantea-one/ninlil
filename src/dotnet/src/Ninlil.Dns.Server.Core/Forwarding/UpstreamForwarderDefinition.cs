using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Forwarding;

/// <summary>
///     This record carries one upstream forwarder's address and protocol details, parsed from its
///     definition string or <c>sdns://</c> stamp by <see cref="IUpstreamForwarderDefinitionParser" />.
///     The <see cref="IUpstreamForwarderClient" /> for <see cref="Protocol" /> sends queries to
///     it.
/// </summary>
/// <param name="Protocol">Which transport protocol this forwarder speaks.</param>
/// <param name="OriginalDefinitionString">The exact string this definition was parsed from, kept for logging and error messages.</param>
/// <param name="Address">
///     The forwarder's IP address. It is <see langword="null" /> for a host-name definition until
///     <c>ForwarderEndpointCache</c> has resolved it, and always <see langword="null" /> for DNS-over-HTTPS.
/// </param>
/// <param name="Port">The forwarder's port.</param>
/// <param name="TlsHostName">
///     The hostname used for SNI and certificate validation of an encrypted forwarder, or
///     <see langword="null" /> when the definition named a bare IP address.
/// </param>
/// <param name="DohUri">
///     The full request URI a <see cref="UpstreamForwarderProtocolEnum.Https" /> forwarder is
///     queried at; <see langword="null" /> for every other protocol.
/// </param>
/// <param name="DnsCryptProviderPublicKey">
///     The upstream's long-term Ed25519 provider public key from the <c>sdns://</c> stamp;
///     <see langword="null" /> except for <see cref="UpstreamForwarderProtocolEnum.DnsCrypt" />.
/// </param>
/// <param name="DnsCryptProviderName">
///     The upstream's provider name from the <c>sdns://</c> stamp, used to fetch its DNSCrypt
///     certificate; <see langword="null" /> except for
///     <see cref="UpstreamForwarderProtocolEnum.DnsCrypt" />.
/// </param>
public sealed record UpstreamForwarderDefinition(
    UpstreamForwarderProtocolEnum Protocol,
    string OriginalDefinitionString,
    IPAddress Address,
    int Port,
    string TlsHostName = null,
    Uri DohUri = null,
    byte[] DnsCryptProviderPublicKey = null,
    string DnsCryptProviderName = null
);
