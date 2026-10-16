using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Forwarding;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Definition;

/// <summary>
///     This class implements <see cref="IUpstreamForwarderDefinitionParser" /> for the forwarder
///     definition strings Ninlil accepts, in the common scheme-prefixed style: a bare address or
///     <c>udp://</c>/<c>tcp://</c> (plain DNS; both mean UDP with TCP fallback),
///     <c>tls://host[:port]</c> (DNS-over-TLS), <c>https://url</c> (DNS-over-HTTPS),
///     <c>quic://host[:port]</c> (DNS-over-QUIC) and <c>sdns://...</c> (a DNSCrypt stamp,
///     handed to <see cref="DnsCryptStampParser" />). <see cref="ForwarderEndpointCache" /> and
///     <c>ForwardersController</c> call it.
///     <para>
///         The parser is purely syntactic and never queries DNS. A host name leaves
///         <see cref="UpstreamForwarderDefinition.Address" /> <see langword="null" /> and is kept
///         in <see cref="UpstreamForwarderDefinition.TlsHostName" />; <see cref="ForwarderEndpointCache" />
///         resolves it in the background. A <c>https://</c> definition never has an address.
///     </para>
///     <para>
///         An IPv6 address with a port is written in brackets (RFC 3986 §3.2.2); an unbracketed
///         IPv6 address is the whole host and uses the default port.
///     </para>
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

        // No recognized scheme: a bare plain-DNS address, such as "1.1.1.1", "1.1.1.1:53" or
        // "[2606:4700:4700::1111]:53".
        return ParseHostPort(
            trimmed,
            prefix: "",
            UpstreamForwarderProtocolEnum.Plain,
            PlainDefaultPort
        );
    }

    /// <summary>
    ///     This method parses an <c>https://</c> definition: a complete DoH URL.
    ///     <c>HttpsForwarderClient</c> connects through <see cref="UpstreamForwarderDefinition.DohUri" />
    ///     alone, so the host is not resolved here and <see cref="UpstreamForwarderDefinition.Address" />
    ///     stays <see langword="null" />.
    /// </summary>
    private static UpstreamForwarderDefinition ParseHttps(string definitionString)
    {
        if (!Uri.TryCreate(definitionString, UriKind.Absolute, out Uri uri))
            throw new FormatException($"'{definitionString}' is not a valid https:// URL.");

        return new UpstreamForwarderDefinition(
            UpstreamForwarderProtocolEnum.Https,
            definitionString,
            null,
            uri.IsDefaultPort ? 443 : uri.Port,
            DohUri: uri
        );
    }

    /// <summary>
    ///     This method parses every <c>&lt;scheme&gt;://host[:port]</c> form (and the bare-address
    ///     form, when <paramref name="prefix" /> is empty). A host name is not resolved: the
    ///     address stays <see langword="null" /> and the name goes to
    ///     <see cref="UpstreamForwarderDefinition.TlsHostName" />.
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

            if (remainder.Length == 0)
                port = defaultPort;
            else if (remainder.StartsWith(':'))
                port = ParsePort(remainder[1..], definitionString);
            else
                throw new FormatException(
                    $"'{definitionString}' must have ':port' after the bracketed IPv6 address."
                );
        }
        else if (
            IPAddress.TryParse(hostPort, out IPAddress wholeAddress)
            && wholeAddress.AddressFamily == AddressFamily.InterNetworkV6
        )
        {
            // The whole text is an IPv6 address, so its last group is not a port.
            host = hostPort;
            port = defaultPort;
        }
        else
        {
            int firstColon = hostPort.IndexOf(':');
            int lastColon = hostPort.LastIndexOf(':');

            if (firstColon != lastColon)
                throw new FormatException(
                    $"'{definitionString}' is not a valid IPv6 address; an IPv6 address with a port must be written as [address]:port."
                );

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
        IPAddress address = isLiteralAddress ? literalAddress : null;

        // No lookup happens here, so the syntax is the only check a host name gets: text that
        // cannot be a host name is rejected at save time rather than failing in the background.
        if (!isLiteralAddress && Uri.CheckHostName(host) != UriHostNameType.Dns)
            throw new FormatException(
                $"'{definitionString}' carries '{host}', which is not a valid host name."
            );

        // The name is what ForwarderEndpointCache resolves, and what TlsForwarderClient and
        // QuicForwarderClient validate the certificate against; for an IP literal they use the
        // address instead.
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
    ///     This method parses a port number, which must be from 1 to 65535.
    /// </summary>
    private static int ParsePort(string portText, string definitionString) =>
        int.TryParse(portText, out int port) && port is > 0 and <= 65535
            ? port
            : throw new FormatException(
                $"'{definitionString}' carries an unparseable port '{portText}'."
            );
}
