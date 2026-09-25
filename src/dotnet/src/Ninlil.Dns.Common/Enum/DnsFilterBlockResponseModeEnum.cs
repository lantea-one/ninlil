using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes how the filtering engine answers a blocked query. It backs
///     <c>DnsFilterOptions.BlockResponseMode</c> and is Ninlil vocabulary, not an IANA-assigned
///     value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsFilterBlockResponseModeEnum>>]
public enum DnsFilterBlockResponseModeEnum
{
    /// <summary>
    ///     The query is answered <c>NXDOMAIN</c>. This is the default.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NxDomain")]
    NxDomain = 0,

    /// <summary>
    ///     The query is answered with an A or AAAA record that points at a configured sinkhole
    ///     address (by default <c>0.0.0.0</c> or <c>::</c>), the approach Pi-hole made common.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SinkholeAddress")]
    SinkholeAddress = 1,
}
