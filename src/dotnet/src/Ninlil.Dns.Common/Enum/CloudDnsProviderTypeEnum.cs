using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the cloud DNS providers a <see cref="ZoneTypeEnum.CloudPrimary" /> or
///     <see cref="ZoneTypeEnum.CloudSecondary" /> zone can sync with. Sync uses the provider's REST
///     API rather than RFC 5936/1996 zone transfer. It is Ninlil vocabulary, not an IANA-assigned
///     value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<CloudDnsProviderTypeEnum>>]
public enum CloudDnsProviderTypeEnum
{
    /// <summary>
    ///     Cloudflare's DNS product, synced via its documented v4 REST API.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Cloudflare")]
    Cloudflare = 0,

    /// <summary>
    ///     Amazon Route 53, synced via its documented REST API.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Route53")]
    Route53 = 1,

    /// <summary>
    ///     DigitalOcean's DNS product, synced via its documented v2 REST API.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DigitalOcean")]
    DigitalOcean = 2,
}
