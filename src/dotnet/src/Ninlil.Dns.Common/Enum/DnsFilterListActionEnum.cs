using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes whether a filter list's entries deny or permit a match. It is
///     Ninlil vocabulary, not an IANA-assigned value.
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
    ///     A match against this list permits the query, response or client. Whether it overrides a
    ///     <see cref="Block" /> list match depends on the configured
    ///     <see cref="DnsFilterListPrecedenceEnum" />.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Allow")]
    Allow = 1,
}
