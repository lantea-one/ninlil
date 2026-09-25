using System.Buffers.Text;
using System.Net;
using System.Text;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Forwarding;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Definition;

/// <summary>
///     This class parses an <c>sdns://</c> DNS Stamp of the plain DNSCrypt type (protocol
///     identifier <c>0x01</c>) for <see cref="UpstreamForwarderDefinitionParser" />. DoH, DoT and
///     ODoH stamps are not accepted, since those protocols have their own <c>tls://</c>,
///     <c>https://</c> and <c>quic://</c> forms. The format follows the stamps specification
///     (<c>https://dnscrypt.info/stamps-specifications</c>), not any implementation, per the
///     clean-room policy.
///     <para>
///         The decoded layout is
///         <c>0x01 || props (8 bytes, little-endian) || LP(addr) || LP(pk) || LP(providerName)</c>,
///         where <c>LP(x) = len(x) (1 byte) || x</c>. Everything after <c>sdns://</c> is
///         base64url without padding (the specification does not say, and published stamps omit
///         it).
///     </para>
///     <para>
///         <b>Provider name.</b> By DNSCrypt convention a stamp's <c>providerName</c> is already
///         the full certificate-discovery name (for example <c>2.dnscrypt-cert.example.com</c>).
///         The server-role <c>DnsCryptOptions.ProviderName</c> is only the zone part, and
///         <c>DnsCryptCertificateDiscoveryResponder.BuildProviderCertificateName</c> adds the
///         prefix. So the forwarder client uses
///         <see cref="UpstreamForwarderDefinition.DnsCryptProviderName" /> as the query name as is,
///         without adding the prefix again.
///     </para>
/// </summary>
public static class DnsCryptStampParser
{
    /// <summary>
    ///     This constant contains the leading byte identifying a plain-DNSCrypt stamp, per the
    ///     stamps specification.
    /// </summary>
    private const byte DnsCryptProtocolId = 0x01;

    /// <summary>
    ///     This constant contains the length, in bytes, of the little-endian props bitmask after
    ///     the protocol identifier. It is skipped, since none of its bits (DNSSEC support,
    ///     no-logging, no-filtering) affect how the client connects.
    /// </summary>
    private const int PropsLength = 8;

    /// <summary>
    ///     This constant contains the length, in bytes, of a DNSCrypt provider's Ed25519 public key.
    /// </summary>
    private const int PublicKeyLength = 32;

    /// <summary>
    ///     This constant contains the port used when a stamp's address has none, the usual
    ///     DNSCrypt default.
    /// </summary>
    private const int DefaultPort = 443;

    /// <summary>
    ///     This method parses a single <c>sdns://</c> plain-DNSCrypt stamp.
    /// </summary>
    /// <param name="stampString">The complete stamp string, including its <c>sdns://</c> prefix.</param>
    /// <returns>The parsed definition, with <see cref="UpstreamForwarderDefinition.Protocol" /> set to <see cref="UpstreamForwarderProtocolEnum.DnsCrypt" />.</returns>
    /// <exception cref="FormatException">
    ///     Thrown when <paramref name="stampString" /> is not a well-formed plain-DNSCrypt stamp.
    /// </exception>
    public static UpstreamForwarderDefinition Parse(string stampString)
    {
        const string prefix = "sdns://";

        if (!stampString.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"'{stampString}' is not an sdns:// stamp.");

        byte[] decoded;

        try
        {
            decoded = Base64Url.DecodeFromChars(stampString.AsSpan(prefix.Length));
        }
        catch (FormatException exception)
        {
            throw new FormatException($"'{stampString}' is not valid base64url.", exception);
        }

        int minimumLength = 1 + PropsLength + 1 + 1 + 1;

        if (decoded.Length < minimumLength || decoded[0] != DnsCryptProtocolId)
            throw new FormatException(
                $"'{stampString}' is not a plain-DNSCrypt (protocol 0x01) stamp."
            );

        int offset = 1 + PropsLength;

        string addr = ReadLengthPrefixedString(decoded, ref offset, stampString);
        byte[] providerPublicKey = ReadLengthPrefixedBytes(decoded, ref offset, stampString);
        string providerName = ReadLengthPrefixedString(decoded, ref offset, stampString);

        if (providerPublicKey.Length != PublicKeyLength)
            throw new FormatException(
                $"'{stampString}' carries a {providerPublicKey.Length}-byte provider public key; expected {PublicKeyLength}."
            );

        (IPAddress address, int port) = ParseAddress(addr, stampString);

        return new UpstreamForwarderDefinition(
            UpstreamForwarderProtocolEnum.DnsCrypt,
            stampString,
            address,
            port,
            DnsCryptProviderPublicKey: providerPublicKey,
            DnsCryptProviderName: providerName
        );
    }

    /// <summary>
    ///     This method reads one <c>LP(x)</c> length-prefixed field's bytes and advances
    ///     <paramref name="offset" /> past it.
    /// </summary>
    private static byte[] ReadLengthPrefixedBytes(
        byte[] decoded,
        ref int offset,
        string stampString
    )
    {
        if (offset >= decoded.Length)
            throw new FormatException(
                $"'{stampString}' ends before its next length-prefixed field."
            );

        int length = decoded[offset];
        offset += 1;

        if (offset + length > decoded.Length)
            throw new FormatException(
                $"'{stampString}' declares a field longer than the remaining stamp."
            );

        byte[] value = decoded[offset..(offset + length)];
        offset += length;

        return value;
    }

    /// <summary>
    ///     This method reads one <c>LP(x)</c> length-prefixed field as a UTF-8 string.
    /// </summary>
    private static string ReadLengthPrefixedString(
        byte[] decoded,
        ref int offset,
        string stampString
    ) => Encoding.UTF8.GetString(ReadLengthPrefixedBytes(decoded, ref offset, stampString));

    /// <summary>
    ///     This method parses a stamp's <c>addr[:port]</c> field: an IPv4 address or a bracketed
    ///     IPv6 address, optionally followed by a port (<see cref="DefaultPort" /> otherwise).
    /// </summary>
    private static (IPAddress Address, int Port) ParseAddress(string addr, string stampString)
    {
        if (string.IsNullOrEmpty(addr))
            throw new FormatException($"'{stampString}' carries an empty address field.");

        if (addr.StartsWith('['))
        {
            int closingBracket = addr.IndexOf(']');

            if (closingBracket < 0)
                throw new FormatException(
                    $"'{stampString}' carries a malformed bracketed IPv6 address."
                );

            string host = addr[1..closingBracket];
            string remainder = addr[(closingBracket + 1)..];
            int port = remainder.StartsWith(':')
                ? ParsePort(remainder[1..], stampString)
                : DefaultPort;

            return (ParseIpAddress(host, stampString), port);
        }

        int lastColon = addr.LastIndexOf(':');

        // A bare IPv4 address has no colon and one with a port has one. The specification does
        // not define an unbracketed IPv6 address; one is split at its last colon, and usually
        // fails to parse.
        if (lastColon < 0)
            return (ParseIpAddress(addr, stampString), DefaultPort);

        string ipPart = addr[..lastColon];
        string portPart = addr[(lastColon + 1)..];

        return (ParseIpAddress(ipPart, stampString), ParsePort(portPart, stampString));
    }

    /// <summary>
    ///     This method parses an IP address, throwing a <see cref="FormatException" /> that names
    ///     the stamp on failure.
    /// </summary>
    private static IPAddress ParseIpAddress(string host, string stampString) =>
        IPAddress.TryParse(host, out IPAddress address)
            ? address
            : throw new FormatException(
                $"'{stampString}' carries an unparseable address '{host}'."
            );

    /// <summary>
    ///     This method parses a port number, which must be from 1 to 65535.
    /// </summary>
    private static int ParsePort(string portText, string stampString) =>
        int.TryParse(portText, out int port) && port is > 0 and <= 65535
            ? port
            : throw new FormatException(
                $"'{stampString}' carries an unparseable port '{portText}'."
            );
}
