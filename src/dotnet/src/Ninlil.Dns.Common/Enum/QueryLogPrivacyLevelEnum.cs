using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the persistent query log's own graduated privacy levels -- a
///     single, unified 0-3 scale controlling how much of an already-answered query's own detail
///     is ever actually written to storage, replacing what would otherwise be two separate,
///     overlapping mechanisms (a Pi-hole-style graduated privacy scale and an AdGuard Home-style
///     standalone "anonymize client IPs" toggle) with one coherent one. Each level is strictly
///     more private than the one before it -- a level's own redaction always includes every
///     redaction the level below it already applies, on top of its own -- so an operator turning
///     this dial up never needs to reason about two independent settings that might disagree.
///     <para>
///         The level in effect at the moment a query is answered is what is actually applied --
///         see <c>Server.QueryLog.DnsQueryLogSubscriber</c>'s own doc comment for why this is a
///         write-time decision, never a read-time display filter: a field never written in the
///         first place can never later be un-redacted, by a level change or anything else, no
///         matter what level is in effect when an entry is later read back.
///     </para>
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<QueryLogPrivacyLevelEnum>>]
public enum QueryLogPrivacyLevelEnum : byte
{
    /// <summary>
    ///     Full detail -- today's original, unchanged behavior, and this scale's own default.
    ///     Every field a query log entry can carry (client address, queried domain name, queried
    ///     type, response code, answer count) is stored exactly as observed.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Full")]
    Full = 0,

    /// <summary>
    ///     Domain names are redacted. The queried domain name is never stored at all (not
    ///     hashed, not truncated -- simply never written); every other field, including the
    ///     client's own address and the queried record type, is stored exactly as observed, so
    ///     aggregate stats such as per-type or per-response-code query counts remain fully
    ///     meaningful.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DomainRedacted")]
    DomainRedacted = 1,

    /// <summary>
    ///     Domain names are redacted (as <see cref="DomainRedacted" />) and the client's own
    ///     identity is hidden: rather than storing the client's source address verbatim, only its
    ///     containing subnet is stored (the address's own trailing bits masked to zero -- the
    ///     last octet for IPv4, the trailing 64 bits for IPv6). See
    ///     <c>Server.QueryLog.DnsQueryLogSubscriber</c>'s own doc comment for why a subnet
    ///     truncation, not a hash, is this level's own client-anonymization mechanism.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ClientAnonymized")]
    ClientAnonymized = 2,

    /// <summary>
    ///     Fully anonymous. Neither the queried domain name nor any form of the client's own
    ///     address (not even a truncated subnet) is ever stored -- only the fields needed for
    ///     aggregate counting (when the query happened, its transport, its queried record type,
    ///     its response code, and its answer count) are retained.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "FullyAnonymous")]
    FullyAnonymous = 3,
}
