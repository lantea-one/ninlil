using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes whether a block/allow list's entries deny or permit a
///     match -- Ninlil's own operational vocabulary, not an IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsFilterListActionEnum>>]
public enum DnsFilterListActionEnum
{
    /// <summary>
    ///     A match against this list denies the query/response/client.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Block")]
    Block = 0,

    /// <summary>
    ///     A match against this list explicitly permits the query/response/client, overriding a
    ///     <see cref="Block" />-list match per whichever precedence rule is currently configured.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Allow")]
    Allow = 1,
}
