using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists which transport protocol an upstream forwarder definition string
///     names. It is Ninlil vocabulary and covers every protocol the forwarding resolver can use to
///     reach an upstream.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<UpstreamForwarderProtocolEnum>>]
public enum UpstreamForwarderProtocolEnum
{
    /// <summary>
    ///     RFC 1035 plain UDP/TCP.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Plain")]
    Plain = 0,

    /// <summary>
    ///     RFC 7858 DNS-over-TLS.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Tls")]
    Tls = 1,

    /// <summary>
    ///     RFC 8484 DNS-over-HTTPS.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Https")]
    Https = 2,

    /// <summary>
    ///     RFC 9250 DNS-over-QUIC.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Quic")]
    Quic = 3,

    /// <summary>
    ///     DNSCrypt v2, addressed via an <c>sdns://</c> stamp.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DnsCrypt")]
    DnsCrypt = 4,
}
