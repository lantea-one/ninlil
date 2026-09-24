// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes which server capability a single <c>DnsAccessControlRuleEntity</c>
///     grants to a client whose source address falls within that rule's own network/CIDR --
///     Ninlil's own operational vocabulary, not an IANA-assigned value (see
///     <see cref="ZoneTypeEnum" />'s own doc comment for the same "long-standing DNS operations
///     terminology, but not a wire value" framing). Marked <see cref="FlagsAttribute" /> so a
///     single rule can grant more than one capability at once (e.g. a trusted LAN that is both
///     allowed to recurse and exempt from rate limiting) without a second, differently-shaped
///     join table -- the generic "a named rule set = network/CIDR + capability(ies) it affects"
///     shape, extensible to a third capability later by adding one more bit.
///     <para>
///         <strong>Deliberately not decorated with <c>[JsonConverter&lt;EnumDescriptionJsonConverter&lt;T&gt;&gt;]</c>
///         the way every other single-valued enum in this namespace is</strong> --
///         that converter (<c>Ninlil.Common.Extension.Converter.EnumDescriptionJsonConverter{TEnum}</c>)
///         writes and reads exactly one <see cref="System.Runtime.Serialization.EnumMemberAttribute" />-mapped
///         string per value; it was never built to round-trip a combined
///         <see cref="FlagsAttribute" /> value
///         and was not extended here to do so. The REST boundary that eventually exposes this
///         property (Phase 5o-10c) is expected to project it into an explicit array-of-strings
///         shape instead (one string per granted capability, suited to a multi-select checkbox
///         group per this project's own UI/UX convention for "which of several fixed options
///         applies"), decoupling the wire shape from the stored bitmask the same way
///         <c>RecordEntity.DataJson</c> already decouples its own always-PascalCase internal
///         storage from the camelCase shape <c>RecordsController</c> projects it into at the REST
///         boundary.
///     </para>
/// </summary>
[Flags]
public enum DnsAccessControlCapabilityEnum
{
    /// <summary>
    ///     This value means the rule grants no capability at all -- never expected to be stored,
    ///     only useful as the identity value for combining flags.
    /// </summary>
    None = 0,

    /// <summary>
    ///     This value means a client matching the rule's own network/CIDR is permitted to use
    ///     this server's recursive resolver, regardless of <c>RecursiveServerOptions.EnableRecursion</c>'s
    ///     own global on/off value -- the first of the two confirmed real consumers this rule
    ///     type exists for (a recursion-gate check in <c>DnsQueryDispatcher</c>, per Phase
    ///     5o-10a's own confirmed call-site finding).
    /// </summary>
    RecursionAccess = 1 << 0,

    /// <summary>
    ///     This value means a client matching the rule's own network/CIDR is exempt from
    ///     <c>Server.RateLimiting</c>'s own per-client-IP token-bucket limiter -- the second of
    ///     the two confirmed real consumers this rule type exists for (a bypass check in
    ///     <c>TokenBucketDnsRateLimiter.TryAcquireAsync</c>, per Phase 5o-10a's own confirmed
    ///     call-site finding), added 2026-09-01 after the original recursion-only framing was
    ///     found to be too narrow.
    /// </summary>
    RateLimitBypass = 1 << 1,
}
