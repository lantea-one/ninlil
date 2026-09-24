using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists what kind of value a single block/allow list's own
///     entries hold, and so what a query is matched against -- Ninlil's own operational
///     vocabulary, not an IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsFilterListKindEnum>>]
public enum DnsFilterListKindEnum
{
    /// <summary>
    ///     Entries are domain names (exact or wildcard); matched against the queried name itself.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Domain")]
    Domain = 0,

    /// <summary>
    ///     Entries are IP addresses or CIDR networks; matched against the resolved answer's own
    ///     A/AAAA record content (Technitium's "response IP blocking") rather than the queried
    ///     name.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ResponseIp")]
    ResponseIp = 1,

    /// <summary>
    ///     Entries are IP addresses or CIDR networks; matched against the querying client's own
    ///     source address -- an access-control mechanism, not content filtering.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ClientSource")]
    ClientSource = 2,
}
