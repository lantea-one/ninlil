using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This enumeration is the outcome of one attempt to refresh the vendor registries.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<MacVendorRefreshResultEnum>>]
public enum MacVendorRefreshResultEnum
{
    /// <summary>At least one registry had changed; a new table was built, persisted, and loaded.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Updated")]
    Updated = 0,

    /// <summary>Every registry was confirmed unchanged; the loaded table stays as it was.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "NotModified")]
    NotModified = 1,

    /// <summary>A download failed or was rejected as implausible; the loaded table stays as it was.</summary>
    [System.Runtime.Serialization.EnumMember(Value = "Failed")]
    Failed = 2,
}
