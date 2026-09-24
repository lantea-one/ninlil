using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists what kind of DNS answer a single <c>DnsRewriteEntity.Target</c>
///     resolves to, computed by classifying the target string itself (<c>IPAddress.TryParse</c>
///     first, otherwise a syntactically valid <see cref="Wire.DnsName" />) rather than stored as
///     an explicit, separately-settable field -- the same "compute a read-only classification
///     fresh from the real, already-validated data" convention <c>ForwarderDetail.Protocol</c>
///     already establishes for a forwarder's own <c>definitionString</c>.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsRewriteTargetKindEnum>>]
public enum DnsRewriteTargetKindEnum
{
    /// <summary>
    ///     The target parses as an IPv4 address -- answered with an
    ///     <see cref="DnsResourceRecordTypeEnum.A" /> record.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "A")]
    A = 0,

    /// <summary>
    ///     The target parses as an IPv6 address -- answered with an
    ///     <see cref="DnsResourceRecordTypeEnum.Aaaa" /> record.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Aaaa")]
    Aaaa = 1,

    /// <summary>
    ///     The target is neither an IPv4 nor an IPv6 address, but is a syntactically valid domain
    ///     name -- answered with a <see cref="DnsResourceRecordTypeEnum.Cname" /> record pointing
    ///     at it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Cname")]
    Cname = 2,
}
