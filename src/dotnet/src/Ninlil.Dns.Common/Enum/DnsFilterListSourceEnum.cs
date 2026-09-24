using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes where a block/allow list's entries come from --
///     Ninlil's own operational vocabulary, not an IANA-assigned value.
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
    ///     Entries are periodically fetched from an external URL (hosts-file, bare-domain or
    ///     adblock-syntax lists, e.g. StevenBlack, HaGeZi or the AdGuard DNS filter) and replaced
    ///     wholesale on each refresh -- an operator
    ///     does not directly edit this list's own entries.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Subscribed")]
    Subscribed = 1,
}
