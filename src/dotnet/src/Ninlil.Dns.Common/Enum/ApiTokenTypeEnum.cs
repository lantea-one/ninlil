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
    ///     A long-lived token that keeps working, unchanged, until it is explicitly revoked or it
    ///     reaches its expiry date. Suited to unattended integrations, such as a dynamic-DNS
    ///     update client, that need to authenticate the same way every time.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Static")]
    Static = 0,

    /// <summary>
    ///     A token that is consumed the first time it is successfully used and can never be used
    ///     again afterward. Suited to workflows such as emailed setup links or single-use
    ///     automation hooks, where a token being reused would itself be a sign of misuse.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "OneTime")]
    OneTime = 1,
}
