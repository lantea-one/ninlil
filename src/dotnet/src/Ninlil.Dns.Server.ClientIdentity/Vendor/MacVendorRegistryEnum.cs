using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This enumeration names the three IEEE Registration Authority MAC address block registries
///     vendor lookup draws on. Each assigns an organization a prefix of a different length out of
///     the 48-bit MAC address space; a longer prefix is a smaller block carved out of a larger one
///     the IEEE itself holds, so a lookup always prefers the longest matching prefix.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<MacVendorRegistryEnum>>]
public enum MacVendorRegistryEnum
{
    /// <summary>MA-L (MAC Address Block Large, the classic OUI): a 24-bit prefix.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "MA-L")]
    MaL = 0,

    /// <summary>MA-M (MAC Address Block Medium): a 28-bit prefix.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "MA-M")]
    MaM = 1,

    /// <summary>MA-S (MAC Address Block Small, formerly OUI-36): a 36-bit prefix.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "MA-S")]
    MaS = 2,
}
