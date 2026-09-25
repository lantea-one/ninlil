using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes which list wins when a single query matches both a
///     <see cref="DnsFilterListActionEnum.Block" /> and a <see cref="DnsFilterListActionEnum.Allow" />
///     list of the same <see cref="DnsFilterListKindEnum" />. It is Ninlil vocabulary, not an
///     IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsFilterListPrecedenceEnum>>]
public enum DnsFilterListPrecedenceEnum
{
    /// <summary>
    ///     An allow-list match always wins. This is the usual ad-blocker and firewall convention,
    ///     and the default.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "AllowWins")]
    AllowWins = 0,

    /// <summary>
    ///     A block-list match always wins, even over an allow-list entry. This is the stricter,
    ///     opt-in choice.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "BlockWins")]
    BlockWins = 1,
}
