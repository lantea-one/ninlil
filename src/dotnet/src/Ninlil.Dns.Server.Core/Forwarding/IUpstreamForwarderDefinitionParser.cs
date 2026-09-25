// Define our namespace.
namespace Ninlil.Dns.Server.Core.Forwarding;

/// <summary>
///     This interface parses one upstream forwarder definition string (a plain address, a
///     <c>udp://</c>, <c>tcp://</c>, <c>tls://</c>, <c>https://</c> or <c>quic://</c> URL, or an
///     <c>sdns://</c> DNSCrypt stamp) into an <see cref="UpstreamForwarderDefinition" />. The
///     same grammar serves the global forwarders (<c>DnsForwarderEntity.DefinitionString</c>)
///     and a forwarder zone's <c>ZoneEntity.ForwarderAddresses</c>.
/// </summary>
public interface IUpstreamForwarderDefinitionParser
{
    /// <summary>
    ///     This method parses a single definition string.
    /// </summary>
    /// <param name="definitionString">The definition string to parse.</param>
    /// <returns>The parsed definition.</returns>
    /// <exception cref="FormatException">
    ///     Thrown when <paramref name="definitionString" /> is not a recognized form.
    /// </exception>
    UpstreamForwarderDefinition Parse(string definitionString);
}
