// Define our namespace.
namespace Ninlil.Dns.Server.Core.Forwarding;

/// <summary>
///     This interface parses a single Technitium/AdGuard Home-style upstream forwarder
///     definition string (a plain address, a <c>udp://</c>/<c>tcp://</c>/<c>tls://</c>/
///     <c>https://</c>/<c>quic://</c> URL, or an <c>sdns://</c> DNSCrypt stamp) into a
///     <see cref="UpstreamForwarderDefinition" />. The exact same definition-string grammar is
///     used both for the server-wide default forwarder set (<c>DnsForwarderEntity.DefinitionString</c>)
///     and a <c>ZoneTypeEnum.Forwarder</c> zone's own per-zone <c>ZoneEntity.ForwarderAddresses</c>.
/// </summary>
public interface IUpstreamForwarderDefinitionParser
{
    /// <summary>
    ///     This method parses a single definition string.
    /// </summary>
    /// <param name="definitionString">The definition string to parse.</param>
    /// <returns>The parsed definition.</returns>
    /// <exception cref="FormatException">
    ///     Thrown when <paramref name="definitionString" /> is not a recognized form -- an
    ///     operator-facing configuration error, not a wire-format one, so this is not the same
    ///     exception type <see cref="Common.Wire.DnsMessageReader" /> throws for a malformed
    ///     message.
    /// </exception>
    UpstreamForwarderDefinition Parse(string definitionString);
}
