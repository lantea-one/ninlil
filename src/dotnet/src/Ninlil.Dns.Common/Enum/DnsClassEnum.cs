using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the DNS CLASS values assigned by IANA. In practice only
///     <see cref="Internet" /> is in use; the others remain for historical or protocol reasons.
///     Ninlil models them so a message that uses one is parsed instead of rejected.
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
    ///     The Chaos class, defined by RFC 1035. Still used for server diagnostic records such as
    ///     <c>version.bind</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "CH")]
    Chaos = 3,

    /// <summary>
    ///     The Hesiod class, defined by RFC 1035, used by MIT's Hesiod naming service. Kept for
    ///     completeness.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "HS")]
    Hesiod = 4,

    /// <summary>
    ///     Not a data class, but the special value RFC 2136 Dynamic Update uses to mean "no class",
    ///     for example when deleting a specific record from an RRset.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NONE")]
    None = 254,

    /// <summary>
    ///     Not a data class, but the special QCLASS value from RFC 1035 that matches a record of
    ///     any class.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ANY")]
    Any = 255,
}
