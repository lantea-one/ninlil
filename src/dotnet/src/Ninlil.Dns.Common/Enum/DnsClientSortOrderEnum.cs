using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration names the orders a page of known clients (<c>DnsClientEntity</c>) can be
///     listed in. Each order is translated into a storage-engine sort, so page boundaries fall
///     where the full ordering would put them. It is Ninlil vocabulary, not an IANA-assigned
///     value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsClientSortOrderEnum>>]
public enum DnsClientSortOrderEnum
{
    /// <summary>
    ///     Oldest registration first. This is the default, and later activity does not change it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Created")]
    Created = 0,

    /// <summary>
    ///     Most recently seen first, using the latest sighting across all of a device's addresses. A
    ///     device never seen sorts after every device that has been.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "LastSeen")]
    LastSeen = 1,

    /// <summary>
    ///     Alphabetical by display name; a device with no display name sorts after every named one.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DisplayName")]
    DisplayName = 2,
}
