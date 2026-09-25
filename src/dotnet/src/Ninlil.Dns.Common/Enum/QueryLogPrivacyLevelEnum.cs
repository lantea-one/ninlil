using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the privacy levels of the persistent query log: one 0-3 scale that
///     controls how much of each answered query is written to storage. It covers both a
///     Pi-hole-style privacy scale and an AdGuard Home-style "anonymize client IPs" toggle with a
///     single setting. Each level applies every redaction of the level below it plus its own, so
///     there are never two settings that could disagree.
///     <para>
///         The level in effect when a query is answered is the one applied. It is a write-time
///         decision, not a display filter: a field that was never written cannot be recovered by
///         a later level change. <c>Server.QueryLog.DnsQueryLogSubscriber</c> explains this.
///     </para>
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<QueryLogPrivacyLevelEnum>>]
public enum QueryLogPrivacyLevelEnum : byte
{
    /// <summary>
    ///     Full detail, and the default. Every field of a query log entry (client address, queried
    ///     name, queried type, response code, answer count) is stored as observed.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Full")]
    Full = 0,

    /// <summary>
    ///     Domain names are redacted. The queried name is not written at all (not hashed or
    ///     truncated). Every other field, including the client address and record type, is stored as
    ///     observed, so per-type and per-response-code statistics still work.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DomainRedacted")]
    DomainRedacted = 1,

    /// <summary>
    ///     Domain names are redacted (as in <see cref="DomainRedacted" />) and the client is
    ///     anonymized: only its subnet is stored, with the last octet (IPv4) or last 64 bits (IPv6)
    ///     set to zero. <c>Server.QueryLog.DnsQueryLogSubscriber</c> explains why this uses subnet
    ///     truncation instead of a hash.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ClientAnonymized")]
    ClientAnonymized = 2,

    /// <summary>
    ///     Fully anonymous. Neither the queried name nor any form of the client address is stored.
    ///     Only the fields needed for aggregate counts remain: time, transport, record type,
    ///     response code and answer count.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "FullyAnonymous")]
    FullyAnonymous = 3,
}
