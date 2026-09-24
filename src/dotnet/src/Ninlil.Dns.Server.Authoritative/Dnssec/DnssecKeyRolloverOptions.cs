// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:DnssecKeyRollover</c> section of the
///     application's configuration and controls the timing parameters
///     <see cref="DnssecKeyRolloverBackgroundService" /> walks a zone's own key states against, per
///     RFC 6781 §4.1.1.1 (Pre-Publish, for a ZSK) and §4.1.2 (Double-Signature, for a KSK).
/// </summary>
public class DnssecKeyRolloverOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:DnssecKeyRollover";

    /// <summary>
    ///     This property contains the assumed delay for a freshly written zone change to actually
    ///     reach every one of this zone's own authoritative servers -- the "propagation delay" term
    ///     RFC 6781 §4.1.1.1/§4.1.1.2/§4.1.2 each add, unchanged, to every one of their own minimum
    ///     wait durations, on top of whichever TTL that specific wait is otherwise bounded by. A
    ///     single-server deployment (this project's own primary target -- see this project's own
    ///     "Deployment target" note) has no real propagation delay of its own, but a conservative,
    ///     non-zero default still guards against a slow secondary/cloud-connector sync elsewhere.
    /// </summary>
    public TimeSpan PropagationDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     This property contains the assumed Time To Live of a real, externally-hosted parent
    ///     zone's own DS record -- used only for a <see cref="Common.Enum.DnssecKeyRoleEnum.Ksk" />
    ///     rollover whose parent is not itself a zone this same server hosts (see
    ///     <see cref="DnssecKeyRolloverBackgroundService" />'s own doc comment for why this project
    ///     has no live way to observe the real value instead). Irrelevant for a locally hosted
    ///     parent, whose own real DS TTL this project already knows exactly
    ///     (<c>ZoneSigningService.ComputeDelegationDsRecordsAsync</c>'s own hardcoded 3600-second
    ///     value).
    /// </summary>
    public TimeSpan ExternalParentDsTtl { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    ///     This property contains how often <see cref="DnssecKeyRolloverBackgroundService" />'s
    ///     background loop wakes up to check whether any signed zone has a key rollover due for its
    ///     own next transition. Deliberately shorter than
    ///     <c>ZoneSigningOptions.PollInterval</c>'s own 6-hour default -- a rollover's own shortest
    ///     wait (a zone using this project's own default 1-hour DNSKEY TTL) can be well under a day,
    ///     so a coarser poll would itself become the dominant source of delay.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(15);
}
