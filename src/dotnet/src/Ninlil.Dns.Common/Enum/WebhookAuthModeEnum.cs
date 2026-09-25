using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the ways a <c>WebhookSubscriptionEntity</c> can authenticate its
///     delivery POST to the subscriber's endpoint. It is Ninlil vocabulary, not an external
///     standard. The mode decides the shape of the JSON that
///     <c>WebhookSubscriptionEntity.AuthCredentialsEncrypted</c> decrypts to, in the same way
///     <see cref="CloudDnsProviderTypeEnum" /> decides the shape of
///     <c>CloudDnsProviderCredentialEntity.CredentialsEncrypted</c>.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<WebhookAuthModeEnum>>]
public enum WebhookAuthModeEnum
{
    /// <summary>
    ///     No authentication is attached to the delivery POST.
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
