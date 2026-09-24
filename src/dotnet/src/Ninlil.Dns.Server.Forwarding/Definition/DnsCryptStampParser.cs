using System.Buffers.Text;
using System.Net;
using System.Text;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Forwarding;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Definition;

/// <summary>
///     This class parses an <c>sdns://</c> DNS Stamp -- specifically only the plain-DNSCrypt
///     stamp type (protocol identifier <c>0x01</c>); DoH/DoT/ODoH stamp types are out of scope,
///     since this project's forwarder definition strings already have their own, separate
///     <c>tls://</c>/<c>https://</c>/<c>quic://</c> forms for those. Wire format sourced
///     directly from the current stamps specification text
///     (<c>https://dnscrypt.info/stamps-specifications</c>), never inferred from any
///     implementation, per this project's clean-room policy.
///     <para>
///         The stamp's own wire layout, exactly as the specification defines it:
///         <c>0x01 || props (8 bytes, little-endian) || LP(addr) || LP(pk) || LP(providerName)</c>,
///         where <c>LP(x) = len(x) (1 byte) || x</c>. The whole byte sequence that follows
///         <c>sdns://</c> is base64url-encoded (no padding assumed, since the spec text does not
///         say either way and every real-world stamp observed omits it).
///     </para>
///     <para>
///         <b>Provider-name convention note:</b> a stamp's own <c>providerName</c> field is, by
///         real-world DNSCrypt convention, already the *complete* certificate-discovery query
///         name (for example <c>2.dnscrypt-cert.example.com</c>) -- unlike this project's own
///         server-role <c>DnsCryptOptions.ProviderName</c>, which is only the zone part, with
///         <c>DnsCryptCertificateDiscoveryResponder.BuildProviderCertificateName</c> prepending
///         the <c>2.dnscrypt-cert.</c> prefix itself. A client parsing an external stamp must
///         use the decoded <see cref="UpstreamForwarderDefinition.DnsCryptProviderName" /> value
///         directly as the discovery query name, and must not prepend that prefix a second time.
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
    ///     This constant contains the length, in bytes, of the little-endian props bitmask that
    ///     follows the protocol identifier -- unused by this parser beyond skipping past it,
    ///     since none of its bits (DNSSEC support, no-logging, no-filtering) affect how a
    ///     forwarder client actually connects.
    /// </summary>
    private const int PropsLength = 8;

    /// <summary>
    ///     This constant contains a DNSCrypt resolver's own Ed25519 public key length, in bytes.
    /// </summary>
    private const int PublicKeyLength = 32;

    /// <summary>
    ///     This constant contains the port a stamp's own address is assumed to name when it
    ///     specifies no port at all -- the conventional DNSCrypt default.
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
    ///     This method reads a single <c>LP(x)</c> length-prefixed field's own raw bytes,
    ///     advancing <paramref name="offset" /> past it.
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
    ///     This method reads a single <c>LP(x)</c> length-prefixed field as a UTF-8 string.
    /// </summary>
    private static string ReadLengthPrefixedString(
        byte[] decoded,
        ref int offset,
        string stampString
    ) => Encoding.UTF8.GetString(ReadLengthPrefixedBytes(decoded, ref offset, stampString));

    /// <summary>
    ///     This method parses a stamp's own <c>addr [:port]</c> address field -- an IPv4 address,
    ///     or a bracketed IPv6 address, optionally followed by a port; the conventional DNSCrypt
    ///     default port when none is given.
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

        // A bare IPv4 address contains no colons at all; an IPv4 address with a port has
        // exactly one. An unbracketed IPv6 address (which contains several colons) is not a
        // form the stamps specification itself defines, so it is deliberately not accepted
        // here.
        if (lastColon < 0)
            return (ParseIpAddress(addr, stampString), DefaultPort);

        string ipPart = addr[..lastColon];
        string portPart = addr[(lastColon + 1)..];

        return (ParseIpAddress(ipPart, stampString), ParsePort(portPart, stampString));
    }

    /// <summary>
    ///     This method parses a bare IP address string, throwing a stamp-specific
    ///     <see cref="FormatException" /> instead of returning a bare parse failure.
    /// </summary>
    private static IPAddress ParseIpAddress(string host, string stampString) =>
        IPAddress.TryParse(host, out IPAddress address)
            ? address
            : throw new FormatException(
                $"'{stampString}' carries an unparseable address '{host}'."
            );

    /// <summary>
    ///     This method parses a port number string, validating it falls within the valid
    ///     1-65535 range before returning it.
    /// </summary>
    private static int ParsePort(string portText, string stampString) =>
        int.TryParse(portText, out int port) && port is > 0 and <= 65535
            ? port
            : throw new FormatException(
                $"'{stampString}' carries an unparseable port '{portText}'."
            );
}
