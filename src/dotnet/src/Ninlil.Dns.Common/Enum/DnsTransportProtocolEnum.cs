using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the transports Ninlil can carry a DNS message over. There is no IANA
///     registry of DNS transports, so each member names its own specification.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsTransportProtocolEnum>>]
public enum DnsTransportProtocolEnum : byte
{
    /// <summary>
    ///     Plain DNS over UDP, defined by RFC 1035. Still the main transport for short queries and
    ///     responses.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Udp")]
    Udp = 0,

    /// <summary>
    ///     Plain DNS over TCP, defined by RFC 1035 and clarified by RFC 7766. Used when a response
    ///     is too large for UDP, or when the message (a zone transfer, for example) needs a reliable,
    ///     ordered byte stream.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Tcp")]
    Tcp = 1,

    /// <summary>
    ///     DNS over TLS (DoT), defined by RFC 7858. Wraps DNS over TCP in a TLS session, so a network
    ///     observer can see that a query happened but not what it asked.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Tls")]
    Tls = 2,

    /// <summary>
    ///     DNS over HTTPS (DoH), defined by RFC 8484. Carries a DNS message as the body of an
    ///     HTTP request, which lets it blend in with ordinary web traffic on the same port.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Https")]
    Https = 3,

    /// <summary>
    ///     DNS over QUIC (DoQ), defined by RFC 9250. The same privacy as <see cref="Tls" />, but over
    ///     UDP-based QUIC, so a query does not wait for a TCP handshake.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Quic")]
    Quic = 4,

    /// <summary>
    ///     DNSCrypt. Encrypts and authenticates a message with a published provider key instead of
    ///     the certificate-authority trust model of TLS and QUIC. Defined by the community DNSCrypt
    ///     specification, not an IETF RFC.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DnsCrypt")]
    DnsCrypt = 5,
}
