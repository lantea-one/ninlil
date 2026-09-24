using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes how a domain query blocked by the filtering engine is
///     answered -- Ninlil's own operational vocabulary, not an IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsFilterBlockResponseModeEnum>>]
public enum DnsFilterBlockResponseModeEnum
{
    /// <summary>
    ///     The query is answered <c>NXDOMAIN</c> -- this project's own default.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NxDomain")]
    NxDomain = 0,

    /// <summary>
    ///     The query is answered with a real but useless A/AAAA record pointing at a configured
    ///     sinkhole address (conventionally <c>0.0.0.0</c>/<c>::</c>) -- the classic Pi-hole
    ///     approach.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "SinkholeAddress")]
    SinkholeAddress = 1,
}
