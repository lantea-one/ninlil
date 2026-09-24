using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes which list wins when a single query matches both a
///     <see cref="DnsFilterListActionEnum.Block" /> and a <see cref="DnsFilterListActionEnum.Allow" />
///     list at once, of the same <see cref="DnsFilterListKindEnum" /> -- Ninlil's own
///     operational vocabulary, not an IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsFilterListPrecedenceEnum>>]
public enum DnsFilterListPrecedenceEnum
{
    /// <summary>
    ///     An explicit allow-list match always wins -- the standard ad-blocker/firewall
    ///     convention, and this project's own default.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "AllowWins")]
    AllowWins = 0,

    /// <summary>
    ///     A block-list match always wins, even over an explicit allow-list entry -- the
    ///     stricter, opt-in choice.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "BlockWins")]
    BlockWins = 1,
}
