using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This enumeration says where the currently loaded vendor table came from.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<MacVendorDataSourceEnum>>]
public enum MacVendorDataSourceEnum
{
    /// <summary>The snapshot compiled into this build, used until a newer download exists.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Embedded")]
    Embedded = 0,

    /// <summary>A copy downloaded from the IEEE registries by this server and persisted to disk.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Downloaded")]
    Downloaded = 1,
}
