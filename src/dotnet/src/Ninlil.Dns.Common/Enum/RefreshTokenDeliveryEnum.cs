using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration says how a completed sign-in hands its refresh token back to the caller.
///     It is Ninlil vocabulary, not an IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<RefreshTokenDeliveryEnum>>]
public enum RefreshTokenDeliveryEnum
{
    /// <summary>
    ///     In the response body, for the caller to store and present itself. This is the default,
    ///     and suits a script or API client with no cookie jar.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Body")]
    Body = 0,

    /// <summary>
    ///     Only in an HttpOnly cookie scoped to the authentication endpoints, never in the body. The
    ///     browser admin console uses this so page script (and so an XSS bug) cannot read the refresh
    ///     token, while a page reload can still renew the session.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Cookie")]
    Cookie = 1,
}
