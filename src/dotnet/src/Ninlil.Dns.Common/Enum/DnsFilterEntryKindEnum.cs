using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes how a single <c>DnsFilterListEntryEntity</c>'s own
///     <c>Value</c> is matched against whatever it's being checked against -- Ninlil's own
///     operational vocabulary, not an IANA-assigned value. This closes a real gap versus Pi-hole
///     (regex lists) and AdGuard Home (extended/wildcard syntax): without it, <c>DnsFilterEngine</c>
///     could only ever match a domain entry exactly or as a parent of the queried name.
///     <para>
///         Every existing entry defaults to <see cref="Domain" /> (its own numeric value, 0, is
///         unchanged from before this enumeration existed), so every pre-existing row keeps
///         matching exactly as it always did.
///     </para>
///     <para>
///         <see cref="Wildcard" />/<see cref="Regex" /> are meaningful only for a
///         <c>DnsFilterListKindEnum.Domain</c> list's own entries -- a
///         <c>ResponseIp</c>/<c>ClientSource</c> list's entries are always IP addresses/CIDR
///         networks, which neither syntax applies to; <c>FilterListEntriesController</c> rejects
///         setting either kind on a non-<c>Domain</c> list's entry at write time.
///     </para>
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsFilterEntryKindEnum>>]
public enum DnsFilterEntryKindEnum
{
    /// <summary>
    ///     The entry's own <c>Value</c> is a literal domain name, matched exactly or as a parent
    ///     of the queried name -- this project's own established ad-blocker convention, e.g. an
    ///     entry of <c>example.com</c> also matches <c>www.example.com</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Domain")]
    Domain = 0,

    /// <summary>
    ///     The entry's own <c>Value</c> is a shell-style glob (<c>*</c> matches any run of zero
    ///     or more characters, <c>?</c> matches exactly one character), matched against the
    ///     queried name's own full presentation form -- AdGuard Home's own "wildcard" entry
    ///     syntax. Internally translated to an anchored <see cref="Regex" />-equivalent pattern
    ///     rather than a second, bespoke matching engine (see <c>DnsFilterEngine</c>'s own doc
    ///     comment for why).
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Wildcard")]
    Wildcard = 1,

    /// <summary>
    ///     The entry's own <c>Value</c> is a .NET regular expression, matched against the queried
    ///     name's own full presentation form -- Pi-hole's own "regex" list syntax. Matching is
    ///     always timeout-guarded (see <c>DnsFilterEngine</c>'s own doc comment) so a malformed
    ///     or catastrophic-backtracking pattern can never stall query answering.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Regex")]
    Regex = 2,
}
