using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes how a persisted API token is allowed to be used.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<ApiTokenTypeEnum>>]
public enum ApiTokenTypeEnum
{
    /// <summary>
    ///     A long-lived token that keeps working until it is revoked or expires. Suited to unattended
    ///     integrations, such as a dynamic-DNS update client, that authenticate the same way every
    ///     time.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Static")]
    Static = 0,

    /// <summary>
    ///     A token that is consumed by its first successful use and never works again. Suited to
    ///     emailed setup links or single-use automation hooks, where reuse would be a sign of misuse.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "OneTime")]
    OneTime = 1,
}
