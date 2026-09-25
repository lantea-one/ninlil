using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists what kind of value a filter list's entries hold, and so what a query
///     is matched against. It is Ninlil vocabulary, not an IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsFilterListKindEnum>>]
public enum DnsFilterListKindEnum
{
    /// <summary>
    ///     Entries are domain names (exact or wildcard), matched against the queried name.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Domain")]
    Domain = 0,

    /// <summary>
    ///     Entries are IP addresses or CIDR networks, matched against the A/AAAA records in the
    ///     resolved answer rather than the queried name (known elsewhere as "response IP
    ///     blocking").
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ResponseIp")]
    ResponseIp = 1,

    /// <summary>
    ///     Entries are IP addresses or CIDR networks, matched against the client's source address.
    ///     This is access control, not content filtering.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ClientSource")]
    ClientSource = 2,
}
