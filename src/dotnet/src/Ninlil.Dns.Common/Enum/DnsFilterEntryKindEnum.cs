using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes how the <c>Value</c> of a <c>DnsFilterListEntryEntity</c> is
///     matched. It is Ninlil vocabulary, not an IANA-assigned value. Besides plain domains it
///     covers the regex lists of Pi-hole and the wildcard syntax of AdGuard Home.
///     <para>
///         <see cref="Domain" /> is 0, so an entry that does not set a kind matches as a plain
///         domain.
///     </para>
///     <para>
///         <see cref="Wildcard" /> and <see cref="Regex" /> only apply to entries of a
///         <c>DnsFilterListKindEnum.Domain</c> list. <c>ResponseIp</c> and <c>ClientSource</c>
///         entries are IP addresses or CIDR networks, and <c>FilterListEntriesController</c>
///         rejects either kind on them.
///     </para>
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsFilterEntryKindEnum>>]
public enum DnsFilterEntryKindEnum
{
    /// <summary>
    ///     The <c>Value</c> is a literal domain name, matched exactly or as a parent of the queried
    ///     name, as ad blockers usually do: <c>example.com</c> also matches <c>www.example.com</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Domain")]
    Domain = 0,

    /// <summary>
    ///     The <c>Value</c> is a shell-style glob (<c>*</c> matches zero or more characters, <c>?</c>
    ///     matches one), matched against the full presentation form of the queried name, like the
    ///     AdGuard Home wildcard syntax. <c>DnsFilterEngine</c> translates it to an anchored regular
    ///     expression instead of using a second matching engine; its doc comment explains why.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Wildcard")]
    Wildcard = 1,

    /// <summary>
    ///     The <c>Value</c> is a .NET regular expression, matched against the full presentation form
    ///     of the queried name, like Pi-hole regex lists. Matching has a timeout (see
    ///     <c>DnsFilterEngine</c>) so a pattern with catastrophic backtracking cannot stall query
    ///     answering.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Regex")]
    Regex = 2,
}
