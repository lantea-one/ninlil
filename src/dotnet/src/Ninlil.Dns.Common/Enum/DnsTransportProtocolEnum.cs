using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the wire transports Ninlil can carry a DNS message over. Unlike
///     the other enumerations in this namespace, there is no single IANA registry for "DNS
///     transports" -- each value here corresponds to its own protocol specification, referenced on
///     the member itself.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsTransportProtocolEnum>>]
public enum DnsTransportProtocolEnum : byte
{
    /// <summary>
    ///     Plain DNS over UDP, defined by RFC 1035. The original, still-dominant transport for
    ///     short queries and responses.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Udp")]
    Udp = 0,

    /// <summary>
    ///     Plain DNS over TCP, defined by RFC 1035 and clarified by RFC 7766. Used when a response
    ///     is too large for UDP, or when the message itself (a zone transfer, for example)
    ///     requires a reliable, ordered byte stream.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Tcp")]
    Tcp = 1,

    /// <summary>
    ///     DNS over TLS (DoT), defined by RFC 7858. Wraps plain DNS-over-TCP inside a TLS session
    ///     so that a network observer can see that a DNS query happened, but not what it asked.
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
    ///     DNS over QUIC (DoQ), defined by RFC 9250. Gets the same privacy benefit as
    ///     <see cref="Tls" />, but over QUIC's UDP-based transport, so a query no longer needs a
    ///     multi-round-trip TCP handshake before it can be sent.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Quic")]
    Quic = 4,

    /// <summary>
    ///     DNSCrypt. Encrypts and authenticates a DNS message using a lightweight, pre-shared
    ///     provider key rather than the certificate-authority-based trust model TLS and QUIC rely
    ///     on. Defined by the community DNSCrypt protocol specification rather than an IETF RFC.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DnsCrypt")]
    DnsCrypt = 5,
}
