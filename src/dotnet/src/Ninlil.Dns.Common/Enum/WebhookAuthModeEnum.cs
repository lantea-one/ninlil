using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the ways a <c>WebhookSubscriptionEntity</c> can authenticate its
///     own outbound delivery POST against the subscriber's endpoint -- Ninlil's own
///     operational vocabulary, not an IANA-assigned or otherwise externally standardized value.
///     Whichever mode is selected determines the shape of the JSON object
///     <c>WebhookSubscriptionEntity.AuthCredentialsEncrypted</c> decrypts to, the same
///     "declared type selects an opaque payload's own shape" pattern
///     <see cref="CloudDnsProviderTypeEnum" /> already establishes for
///     <c>CloudDnsProviderCredentialEntity.CredentialsEncrypted</c>.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<WebhookAuthModeEnum>>]
public enum WebhookAuthModeEnum
{
    /// <summary>
    ///     No authentication is attached to the outbound delivery POST at all.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "None")]
    None = 0,

    /// <summary>
    ///     An <c>Authorization: Bearer &lt;token&gt;</c> header is attached to the outbound
    ///     delivery POST. <c>AuthCredentialsEncrypted</c> decrypts to <c>{"BearerToken":"..."}</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Bearer")]
    Bearer = 1,

    /// <summary>
    ///     An RFC 7617 HTTP Basic <c>Authorization</c> header is attached to the outbound
    ///     delivery POST. <c>AuthCredentialsEncrypted</c> decrypts to
    ///     <c>{"Username":"...","Password":"..."}</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Basic")]
    Basic = 2,
}
