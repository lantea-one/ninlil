using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes where a filter list's entries come from. It is Ninlil
///     vocabulary, not an IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsFilterListSourceEnum>>]
public enum DnsFilterListSourceEnum
{
    /// <summary>
    ///     Entries are entered and managed directly by an operator.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Local")]
    Local = 0,

    /// <summary>
    ///     Entries are fetched from an external URL on a schedule (hosts-file, bare-domain or
    ///     adblock-syntax lists, such as StevenBlack, HaGeZi or the AdGuard DNS filter) and replaced as
    ///     a whole on each refresh. Operators do not edit these entries directly.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Subscribed")]
    Subscribed = 1,
}
