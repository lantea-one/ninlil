using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the DNS CLASS values assigned by IANA. In practice the Internet
///     effectively has one class -- <see cref="Internet" /> -- and the others survive only for
///     historical or protocol-plumbing reasons; Ninlil still models them so that a message
///     using one of them is parsed correctly instead of being rejected outright.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsClassEnum>>]
public enum DnsClassEnum : ushort
{
    /// <summary>
    ///     The Internet class, defined by RFC 1035. Every record Ninlil serves uses this
    ///     class.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "IN")]
    Internet = 1,

    /// <summary>
    ///     The Chaos class, defined by RFC 1035. Still used in the wild for a server's own
    ///     diagnostic records, such as <c>version.bind</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CH")]
    Chaos = 3,

    /// <summary>
    ///     The Hesiod class, defined by RFC 1035. Historically used by MIT's Hesiod naming
    ///     service; kept only for completeness.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "HS")]
    Hesiod = 4,

    /// <summary>
    ///     Not a real class, but the special QCLASS value RFC 2136 Dynamic Update uses to mean
    ///     "no class restriction" -- for example, when deleting a record set outright.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NONE")]
    None = 254,

    /// <summary>
    ///     Not a real class, but the special QCLASS value a client sends to match a record
    ///     regardless of its class, defined by RFC 1035.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ANY")]
    Any = 255,
}
