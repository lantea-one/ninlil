using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Forwarding;

/// <summary>
///     This class carries a single upstream forwarder's own address and protocol-specific
///     connection details, already parsed out of its Technitium/AdGuard Home-style definition
///     string (or <c>sdns://</c> DNSCrypt stamp) by an <see cref="IUpstreamForwarderDefinitionParser" />.
///     One <see cref="IUpstreamForwarderClient" />, matching <see cref="Protocol" />, knows how
///     to actually forward a query against it.
/// </summary>
/// <param name="Protocol">Which transport protocol this forwarder speaks.</param>
/// <param name="OriginalDefinitionString">The exact string this definition was parsed from, kept for logging and error messages.</param>
/// <param name="Address">The forwarder's own IP address.</param>
/// <param name="Port">The forwarder's own port.</param>
/// <param name="TlsHostName">
///     The hostname a <see cref="UpstreamForwarderProtocolEnum.Tls" /> forwarder's certificate is
///     validated against (SNI and certificate validation both use this) -- <see langword="null" />
///     when the definition string named a bare IP address, in which case validation falls back
///     to the certificate's own IP address SAN instead.
/// </param>
/// <param name="DohUri">
///     The full request URI a <see cref="UpstreamForwarderProtocolEnum.Https" /> forwarder is
///     queried at; <see langword="null" /> for every other protocol.
/// </param>
/// <param name="DnsCryptProviderPublicKey">
///     The upstream's own long-term Ed25519 provider public key, decoded from an <c>sdns://</c>
///     stamp; <see langword="null" /> for every protocol except
///     <see cref="UpstreamForwarderProtocolEnum.DnsCrypt" />.
/// </param>
/// <param name="DnsCryptProviderName">
///     The upstream's own provider name, decoded from an <c>sdns://</c> stamp -- needed to
///     re-fetch a fresh certificate if the one the stamp itself may carry has expired;
///     <see langword="null" /> for every protocol except
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
