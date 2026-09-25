using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists what kind of DNS answer a <c>DnsRewriteEntity.Target</c> produces.
///     It is not stored. It is computed by classifying the target string: first
///     <c>IPAddress.TryParse</c>, otherwise a valid <see cref="Wire.DnsName" />.
///     <c>ForwarderDetail.Protocol</c> is derived from a forwarder definition string the same
///     way.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsRewriteTargetKindEnum>>]
public enum DnsRewriteTargetKindEnum
{
    /// <summary>
    ///     The target parses as an IPv4 address and is answered with an
    ///     <see cref="DnsResourceRecordTypeEnum.A" /> record.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "A")]
    A = 0,

    /// <summary>
    ///     The target parses as an IPv6 address and is answered with an
    ///     <see cref="DnsResourceRecordTypeEnum.Aaaa" /> record.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Aaaa")]
    Aaaa = 1,

    /// <summary>
    ///     The target is not an IP address but is a valid domain name. It is answered with a
    ///     <see cref="DnsResourceRecordTypeEnum.Cname" /> record pointing at it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Cname")]
    Cname = 2,
}
