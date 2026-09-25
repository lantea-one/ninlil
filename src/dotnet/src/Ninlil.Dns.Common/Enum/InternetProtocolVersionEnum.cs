using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration distinguishes IPv4 from IPv6 wherever data, such as a DHCP lease, needs to
///     say which address family it belongs to.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<InternetProtocolVersionEnum>>]
public enum InternetProtocolVersionEnum
{
    /// <summary>
    ///     Internet Protocol version 4, defined by RFC 791.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "IPv4")]
    IPv4 = 4,

    /// <summary>
    ///     Internet Protocol version 6, defined by RFC 8200.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "IPv6")]
    IPv6 = 6,
}
