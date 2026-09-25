// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes which server capabilities a <c>DnsAccessControlRuleEntity</c>
///     grants to clients whose source address falls in the rule's network. It is Ninlil
///     vocabulary, not an IANA-assigned value. It is a <see cref="FlagsAttribute" /> enum so one
///     rule can grant several capabilities, such as a trusted LAN that may recurse and is exempt
///     from rate limiting. A new capability is one more bit.
///     <para>
///         Unlike the other enums in this namespace it has no
///         <c>[JsonConverter&lt;EnumDescriptionJsonConverter&lt;T&gt;&gt;]</c> attribute. That
///         converter maps one <see cref="System.Runtime.Serialization.EnumMemberAttribute" />
///         string per value and cannot round-trip a combined flags value. The REST API exposes the
///         flags as an array of strings instead (<c>DnsAccessControlRuleModels</c>), one per
///         granted capability.
///     </para>
/// </summary>
[Flags]
public enum DnsAccessControlCapabilityEnum
{
    /// <summary>
    ///     The rule grants no capability. Not expected to be stored; it is the identity value for
    ///     combining flags.
    /// </summary>
    None = 0,

    /// <summary>
    ///     A matching client may use the recursive resolver even when
    ///     <c>RecursiveServerOptions.EnableRecursion</c> is off. <c>RecursiveQueryResolver</c> checks
    ///     it.
    /// </summary>
    RecursionAccess = 1 << 0,

    /// <summary>
    ///     A matching client is exempt from the rate limits in <c>Server.RateLimiting</c>.
    ///     <c>TokenBucketDnsRateLimiter</c> checks it in both its per-client and per-subnet limits.
    /// </summary>
    RateLimitBypass = 1 << 1,
}
