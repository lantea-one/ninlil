using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Forwarding;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Definition;

/// <summary>
///     This class implements <see cref="IUpstreamForwarderDefinitionParser" /> for every
///     Technitium/AdGuard Home-style definition-string form this project supports: a bare
///     address (plain UDP/TCP, the conventional no-scheme form), <c>udp://</c>/<c>tcp://</c>
///     (also plain), <c>tls://host[:port]</c> (DNS-over-TLS), <c>https://url</c>
///     (DNS-over-HTTPS), <c>quic://host[:port]</c> (DNS-over-QUIC), and <c>sdns://...</c>
///     (a DNSCrypt stamp, delegated to <see cref="DnsCryptStampParser" />).
/// </summary>
public sealed class UpstreamForwarderDefinitionParser : IUpstreamForwarderDefinitionParser
{
    private const int PlainDefaultPort = 53;
    private const int TlsDefaultPort = 853;
    private const int QuicDefaultPort = 853;

    /// <inheritdoc />
    public UpstreamForwarderDefinition Parse(string definitionString)
    {
        if (string.IsNullOrWhiteSpace(definitionString))
            throw new FormatException("A forwarder definition string cannot be empty.");

        string trimmed = definitionString.Trim();

        if (trimmed.StartsWith("sdns://", StringComparison.OrdinalIgnoreCase))
            return DnsCryptStampParser.Parse(trimmed);

        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return ParseHttps(trimmed);

        if (trimmed.StartsWith("tls://", StringComparison.OrdinalIgnoreCase))
            return ParseHostPort(
                trimmed,
                "tls://",
                UpstreamForwarderProtocolEnum.Tls,
                TlsDefaultPort
            );

        if (trimmed.StartsWith("quic://", StringComparison.OrdinalIgnoreCase))
            return ParseHostPort(
                trimmed,
                "quic://",
                UpstreamForwarderProtocolEnum.Quic,
                QuicDefaultPort
            );

        if (trimmed.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
            return ParseHostPort(
                trimmed,
                "udp://",
                UpstreamForwarderProtocolEnum.Plain,
                PlainDefaultPort
            );

        if (trimmed.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
            return ParseHostPort(
                trimmed,
                "tcp://",
                UpstreamForwarderProtocolEnum.Plain,
                PlainDefaultPort
            );

        // No recognized scheme: a bare address, the conventional plain-DNS form (e.g. "1.1.1.1"
        // or "1.1.1.1:53" or "[2606:4700:4700::1111]:53").
        return ParseHostPort(
            trimmed,
            prefix: "",
            UpstreamForwarderProtocolEnum.Plain,
            PlainDefaultPort
        );
    }

    /// <summary>
    ///     This method parses <c>https://</c>'s own definition string: a complete DoH URL. The
    ///     URL's own host and port are still resolved into <see cref="UpstreamForwarderDefinition.Address" />/
    ///     <see cref="UpstreamForwarderDefinition.Port" />, even though <c>HttpsForwarderClient</c>
    ///     itself only ever uses <see cref="UpstreamForwarderDefinition.DohUri" /> to actually
    ///     connect -- a future health-checker needs a real address/port to probe reachability
    ///     independently of the DoH-specific request itself.
    /// </summary>
    private static UpstreamForwarderDefinition ParseHttps(string definitionString)
    {
        if (!Uri.TryCreate(definitionString, UriKind.Absolute, out Uri uri))
            throw new FormatException($"'{definitionString}' is not a valid https:// URL.");

        IPAddress address = ResolveHost(uri.Host, definitionString);

        return new UpstreamForwarderDefinition(
            UpstreamForwarderProtocolEnum.Https,
            definitionString,
            address,
            uri.IsDefaultPort ? 443 : uri.Port,
            DohUri: uri
        );
    }

    /// <summary>
    ///     This method parses every <c>&lt;scheme&gt;://host[:port]</c> form (and the
    ///     no-scheme bare-address form, when <paramref name="prefix" /> is empty), resolving a
    ///     hostname to a real address up front -- a forwarder is configured once, not re-resolved
    ///     per query.
    /// </summary>
    private static UpstreamForwarderDefinition ParseHostPort(
        string definitionString,
        string prefix,
        UpstreamForwarderProtocolEnum protocol,
        int defaultPort
    )
    {
        string hostPort = definitionString[prefix.Length..];

        string host;
        int port;

        if (hostPort.StartsWith('['))
        {
            int closingBracket = hostPort.IndexOf(']');

            if (closingBracket < 0)
                throw new FormatException(
                    $"'{definitionString}' carries a malformed bracketed IPv6 address."
                );

            host = hostPort[1..closingBracket];
            string remainder = hostPort[(closingBracket + 1)..];
            port = remainder.StartsWith(':')
                ? ParsePort(remainder[1..], definitionString)
                : defaultPort;
        }
        else
        {
            int lastColon = hostPort.LastIndexOf(':');

            if (lastColon < 0)
            {
                host = hostPort;
                port = defaultPort;
            }
            else
            {
                host = hostPort[..lastColon];
                port = ParsePort(hostPort[(lastColon + 1)..], definitionString);
            }
        }

        if (string.IsNullOrEmpty(host))
            throw new FormatException($"'{definitionString}' carries no host/address at all.");

        bool isLiteralAddress = IPAddress.TryParse(host, out IPAddress literalAddress);
        IPAddress address = isLiteralAddress ? literalAddress : ResolveHost(host, definitionString);

        // A bare IP address never needs a separate SNI/certificate-validation hostname; a real
        // hostname does, for both TlsForwarderClient and QuicForwarderClient.
        string tlsHostName = isLiteralAddress ? null : host;

        return new UpstreamForwarderDefinition(
            protocol,
            definitionString,
            address,
            port,
            TlsHostName: tlsHostName
        );
    }

    /// <summary>
    ///     This method resolves a hostname to a real IP address at parse (configuration-load)
    ///     time, once -- not per query.
    /// </summary>
    private static IPAddress ResolveHost(string host, string definitionString)
    {
        try
        {
            IPAddress[] addresses = System.Net.Dns.GetHostAddresses(host);

            if (addresses.Length == 0)
                throw new FormatException(
                    $"'{definitionString}' names a host ('{host}') that resolved to no addresses."
                );

            return addresses[0];
        }
        catch (SocketException exception)
        {
            throw new FormatException(
                $"'{definitionString}' names a host ('{host}') that could not be resolved.",
                exception
            );
        }
    }

    /// <summary>
    ///     This method parses a port number string, validating it falls within the valid
    ///     1-65535 range before returning it.
    /// </summary>
    private static int ParsePort(string portText, string definitionString) =>
        int.TryParse(portText, out int port) && port is > 0 and <= 65535
            ? port
            : throw new FormatException(
                $"'{definitionString}' carries an unparseable port '{portText}'."
            );
}
