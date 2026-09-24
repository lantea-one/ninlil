using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration names the orderings a page of known clients (<c>DnsClientEntity</c>) can
///     be listed in. Every ordering is translated into the storage engine's own sort, so a page
///     boundary always falls in the same place the full ordering would put it. Ninlil's own
///     presentation vocabulary, not an IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsClientSortOrderEnum>>]
public enum DnsClientSortOrderEnum
{
    /// <summary>
    ///     Oldest registration first -- the stable, default ordering, unaffected by later activity.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Created")]
    Created = 0,

    /// <summary>
    ///     Most recently seen first, by the latest sighting across every one of a device's own
    ///     addresses; a device never seen at all sorts after every device that has been.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "LastSeen")]
    LastSeen = 1,

    /// <summary>
    ///     Alphabetical by display name; a device with no display name sorts after every named one.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DisplayName")]
    DisplayName = 2,
}
