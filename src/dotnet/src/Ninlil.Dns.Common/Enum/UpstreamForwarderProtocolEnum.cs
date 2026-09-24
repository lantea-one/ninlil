using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists which transport protocol a single upstream forwarder definition
///     string names -- Ninlil's own operational vocabulary, matching every protocol this
///     project's own transports already speak (plus plain DNSCrypt, which this project has no
///     listener-side name for since <c>Ninlil.Dns.Protocol.DnsCrypt</c> already claims that
///     name for the server role).
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
